using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Freezia.Native;

namespace Freezia.Core;

public enum HotkeyAction
{
    CaptureRegion,
    CaptureScreen,
    CaptureAllScreens,
}

/// <summary>
/// Global keyboard handling via a WH_KEYBOARD_LL hook that runs on its own thread
/// (so a busy UI thread can never make Windows drop the hook).
/// Three modes:
///  1. Normal: matches configured hotkeys, swallows them (so PrintScreen does not open Snipping Tool).
///  2. Capture mode: while the region overlay is open, routes overlay keys (Esc, Enter, Ctrl+C...) to it,
///     even if the overlay does not have keyboard focus.
///  3. Recording: the settings window records the next key combination.
/// All callbacks are marshalled to the UI dispatcher.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly Dispatcher _ui;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private NativeMethods.LowLevelKeyboardProc? _proc; // keep delegate alive (GC)
    private readonly ManualResetEventSlim _started = new(false);

    // State shared with the hook thread. Snapshots are replaced atomically.
    private volatile Dictionary<Hotkey, HotkeyAction> _bindings = new();
    private volatile Action<int, HotkeyModifiers>? _captureHandler;
    private volatile RecordingCallbacks? _recording;
    private volatile bool _suspended;

    // Hook-thread only state
    private readonly HashSet<int> _swallowedDown = new();
    private readonly HashSet<int> _keysDown = new();
    private bool _recWinDown;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public event Action<HotkeyAction>? Triggered;

    private sealed record RecordingCallbacks(Action<Hotkey> OnKey, Action<HotkeyModifiers> OnModifiers, Action OnCancel, Action OnClear);

    public HotkeyManager(Dispatcher ui)
    {
        _ui = ui;
    }

    public bool Start()
    {
        if (_thread != null) return IsInstalled;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "Freezia keyboard hook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
        return IsInstalled;
    }

    public void SetBindings(AppSettings s)
    {
        var map = new Dictionary<Hotkey, HotkeyAction>();
        void Add(Hotkey h, HotkeyAction a)
        {
            if (h != null && !h.IsEmpty && !map.ContainsKey(h)) map[h] = a;
        }
        Add(s.HotkeyRegion, HotkeyAction.CaptureRegion);
        Add(s.HotkeyScreen, HotkeyAction.CaptureScreen);
        Add(s.HotkeyAllScreens, HotkeyAction.CaptureAllScreens);
        _bindings = map;
    }

    /// <summary>Temporarily disable hotkey actions (e.g. while a modal dialog is open).</summary>
    public bool Suspended
    {
        get => _suspended;
        set => _suspended = value;
    }

    /// <summary>Route overlay keys to <paramref name="handler"/> (UI thread). Pass null to leave capture mode.</summary>
    public void SetCaptureHandler(Action<int, HotkeyModifiers>? handler) => _captureHandler = handler;

    public void BeginRecording(Action<Hotkey> onKey, Action<HotkeyModifiers> onModifiers, Action onCancel, Action onClear) =>
        _recording = new RecordingCallbacks(onKey, onModifiers, onCancel, onClear);

    public void EndRecording() => _recording = null;

    public bool IsRecording => _recording != null;

    // ---------------- hook thread ----------------

    private void HookThread()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        // Make sure the thread has a message queue before anyone posts to it.
        NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _proc = HookProc;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            Logger.Error($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        else
            Logger.Info("Keyboard hook installed");
        _started.Set();

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private HotkeyModifiers CurrentModifiers(bool includeRecordedWin)
    {
        var m = HotkeyModifiers.None;
        if (IsDown(0x11)) m |= HotkeyModifiers.Ctrl;
        if (IsDown(0x10)) m |= HotkeyModifiers.Shift;
        if (IsDown(0x12)) m |= HotkeyModifiers.Alt;
        if (IsDown(0x5B) || IsDown(0x5C) || (includeRecordedWin && _recWinDown)) m |= HotkeyModifiers.Win;
        return m;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                if (Handle((int)wParam, Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam)))
                    return (IntPtr)1;
            }
            catch (Exception ex)
            {
                Logger.Error("Hook error", ex);
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <returns>true to swallow the key event.</returns>
    private bool Handle(int msg, NativeMethods.KBDLLHOOKSTRUCT k)
    {
        int vk = (int)k.vkCode;
        // Our own Start-menu suppression key. Other injected input (on-screen keyboards, remappers) is handled normally.
        if (vk == StartMenuSuppressKey && (k.flags & NativeMethods.LLKHF_INJECTED) != 0) return false;

        bool down = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
        bool up = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;

        if (up)
        {
            _keysDown.Remove(vk);
            if (vk is 0x5B or 0x5C && _recWinDown)
            {
                _recWinDown = false;
                var rec0 = _recording;
                if (rec0 != null) Post(() => rec0.OnModifiers(CurrentModifiersSafe()));
                return true;
            }
            if (_swallowedDown.Remove(vk)) return true;
            var recUp = _recording;
            if (recUp != null && Hotkey.IsModifierKey(vk))
            {
                var mods = CurrentModifiers(true);
                mods &= ~ModifierOf(vk);
                Post(() => recUp.OnModifiers(mods));
            }
            return false;
        }
        if (!down) return false;

        bool repeat = !_keysDown.Add(vk);

        // ---- 3. Recording ----
        var rec = _recording;
        if (rec != null)
        {
            if (vk is 0x5B or 0x5C)
            {
                // Swallow Win so the Start menu does not open while recording.
                _recWinDown = true;
                var m0 = CurrentModifiers(true);
                Post(() => rec.OnModifiers(m0));
                return true;
            }
            if (Hotkey.IsModifierKey(vk))
            {
                var m1 = CurrentModifiers(true) | ModifierOf(vk);
                Post(() => rec.OnModifiers(m1));
                return false;
            }
            _swallowedDown.Add(vk);
            if (repeat) return true;
            var mods = CurrentModifiers(true);
            if (mods == HotkeyModifiers.None && vk == 0x1B) Post(rec.OnCancel);           // Esc
            else if (mods == HotkeyModifiers.None && vk == 0x08) Post(rec.OnClear);       // Backspace
            else
            {
                var hk = new Hotkey(mods, vk);
                Post(() => rec.OnKey(hk));
            }
            return true;
        }

        if (Hotkey.IsModifierKey(vk)) return false;

        var modifiers = CurrentModifiers(false);

        // ---- 2. Capture overlay ----
        var cap = _captureHandler;
        if (cap != null)
        {
            if (_bindings.ContainsKey(new Hotkey(modifiers, vk)))
            {
                _swallowedDown.Add(vk); // ignore hotkeys while already capturing
                return true;
            }
            if (IsCaptureKey(vk, modifiers))
            {
                _swallowedDown.Add(vk);
                bool isArrow = vk is >= 0x25 and <= 0x28;
                if (!repeat || isArrow) Post(() => cap(vk, modifiers));
                return true;
            }
            return false;
        }

        // ---- 1. Normal hotkeys ----
        if (_suspended) return false;
        if (_bindings.TryGetValue(new Hotkey(modifiers, vk), out var action))
        {
            _swallowedDown.Add(vk);
            if (repeat) return true;
            if (modifiers.HasFlag(HotkeyModifiers.Win)) SuppressStartMenu();
            Post(() => Triggered?.Invoke(action));
            return true;
        }
        return false;
    }

    private HotkeyModifiers CurrentModifiersSafe() => CurrentModifiers(true);

    private static HotkeyModifiers ModifierOf(int vk) => vk switch
    {
        0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Ctrl,
        0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
        0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
        0x5B or 0x5C => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    /// <summary>Keys the capture overlay understands.</summary>
    private static bool IsCaptureKey(int vk, HotkeyModifiers m)
    {
        bool ctrl = m.HasFlag(HotkeyModifiers.Ctrl);
        return vk switch
        {
            0x1B => true,                          // Esc
            0x0D => true,                          // Enter
            0x43 => ctrl,                          // Ctrl+C
            0x53 => ctrl,                          // Ctrl+S / Ctrl+Shift+S
            0x45 or 0x46 or 0x41 => !ctrl,         // E, F, A
            >= 0x25 and <= 0x28 => true,           // arrows
            _ => false,
        };
    }

    /// <summary>
    /// After a Win+key hotkey we swallowed the key, so Windows would see a lone Win press and open Start
    /// on release. Injecting an unassigned key in between prevents that.
    /// </summary>
    private const int StartMenuSuppressKey = 0xE8;

    private static void SuppressStartMenu()
    {
        var inputs = new NativeMethods.INPUT[2];
        inputs[0].type = NativeMethods.INPUT_KEYBOARD;
        inputs[0].U.ki = new NativeMethods.KEYBDINPUT { wVk = StartMenuSuppressKey };
        inputs[1].type = NativeMethods.INPUT_KEYBOARD;
        inputs[1].U.ki = new NativeMethods.KEYBDINPUT { wVk = StartMenuSuppressKey, dwFlags = NativeMethods.KEYEVENTF_KEYUP };
        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private void Post(Action a) => _ui.BeginInvoke(a, DispatcherPriority.Input);

    public void Dispose()
    {
        if (_thread != null && _threadId != 0)
        {
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(1000);
        }
        _thread = null;
        _started.Dispose();
    }
}
