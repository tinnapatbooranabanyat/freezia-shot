using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Freezia.Native;
using System.Windows.Input;
using Freezia.Core;
using Freezia.Editor;
using Freezia.UI;

namespace Freezia.Capture;

public enum CaptureOutcome
{
    Cancel,
    Copy,
    Save,
    SaveAs,
    Edit,
    /// <summary>Whole monitor (from F / "เต็มจอนี้").</summary>
    FullScreen,
    /// <summary>All monitors (from A / "ทุกจอ").</summary>
    AllScreens,
    /// <summary>Float the selection on screen (P).</summary>
    Pin,
    /// <summary>Read text in the selection (T).</summary>
    Ocr,
    /// <summary>Read QR codes / barcodes in the selection (Q).</summary>
    Qr,
    /// <summary>Screen color picker: a click picked <see cref="CaptureSession.PickedColor"/>.</summary>
    ColorPicked,
}

public enum DragMode
{
    None,
    Creating,
    Moving,
    Resizing,
}

internal enum SelectionHandle
{
    NW, N, NE, E, SE, S, SW, W, None,
}

/// <summary>
/// One region-capture session: owns the frozen screenshot, one overlay per monitor and the
/// selection rectangle (physical virtual-screen pixels, so selections may span monitors).
/// </summary>
public sealed class CaptureSession
{
    private readonly List<OverlayWindow> _overlays = new();
    private readonly List<MonitorInfo> _monitors;
    private bool _finished;

    // drag state
    private int _startX, _startY;
    private PxRect _startSel;
    private SelectionHandle _handle = SelectionHandle.None;

    public PixelImage Shot { get; }
    public PxRect Selection { get; private set; }
    public bool HasSelection => !Selection.IsEmpty;
    public DragMode Mode { get; private set; }
    public (int X, int Y) Cursor { get; private set; }
    public bool ShowLoupe => PickColorMode || Mode is DragMode.Creating or DragMode.Resizing || (!HasSelection && Mode == DragMode.None);

    public MonitorInfo? LabelMonitor { get; private set; }
    public MonitorInfo? ActionMonitor { get; private set; }

    /// <summary>Raised once. The image is null when cancelled.</summary>
    public event Action<CaptureOutcome, PixelImage?>? Finished;

    /// <summary>Screen color picker: no selection, the loupe follows the mouse, a click picks a color.</summary>
    public bool PickColorMode { get; init; }

    /// <summary>Drawing tools on the overlay (settings: "show drawing tools on the capture screen").</summary>
    public bool ToolsEnabled { get; init; }

    /// <summary>Needed for the drawing tools (style, saved colors).</summary>
    public SettingsService? Settings { get; init; }

    /// <summary>Lightshot-style drawing on the overlay (null when the tools are off).</summary>
    public CaptureEditor? Editor { get; private set; }

    /// <summary>The keyboard must go to a text box (typing, dialog) instead of the capture shortcuts.</summary>
    public event Action<bool>? TextInputChanged;

    /// <summary>Drawings handed to the editor window (outcome Edit), moved into its image coordinates.</summary>
    public List<Annotation>? HandOverItems { get; private set; }
    public System.Windows.Vector HandOverShift { get; private set; }

    /// <summary>The selection covers monitors with different DPI: drawing is paused there.</summary>
    public bool IsCrossDpi
    {
        get
        {
            if (!HasSelection) return false;
            double? scale = null;
            foreach (var m in _monitors)
            {
                if (m.Bounds.Intersect(Selection).IsEmpty) continue;
                if (scale == null) scale = m.Scale;
                else if (Math.Abs(scale.Value - m.Scale) > 0.001) return true;
            }
            return false;
        }
    }

    /// <summary>The frame's resize handles are under this screen point.</summary>
    internal bool IsOnHandle(int x, int y) => HasSelection && Mode == DragMode.None && HitHandle(x, y) != SelectionHandle.None;
    public System.Windows.Media.Color PickedColor { get; private set; }
    public bool PickedAsRgb { get; private set; }

    public CaptureSession(PixelImage shot, List<MonitorInfo> monitors, List<WindowRegion>? windows = null)
    {
        Shot = shot;
        _monitors = monitors;
        _windows = windows ?? new List<WindowRegion>();
    }

    // ---------------- hover: window / UI element under the mouse ----------------

    private readonly List<WindowRegion> _windows;
    private List<PxRect> _hoverChain = new();
    private int _hoverLevel;
    private IntPtr _hoverHwnd;
    private bool _hoverIsElement;
    private CancellationTokenSource? _elementCts;
    private bool _elementBusy;
    private DispatcherTimer? _ctrlTimer;
    private PxRect? _clickHover;
    private bool _lastCtrl;

    /// <summary>Rectangle a single click would select (window, UI element or monitor).</summary>
    public PxRect? Hover => _hoverChain.Count > 0 && !HasSelection && Mode == DragMode.None
        ? _hoverChain[Math.Clamp(_hoverLevel, 0, _hoverChain.Count - 1)]
        : null;

    public bool HoverIsElement => _hoverIsElement;

    private static bool CtrlDown => (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0;

    private PxRect Clip(PxRect r) => r.Intersect(Shot.Bounds);

    /// <summary>Plain hover = whole window; Ctrl held = the UI element under the mouse.</summary>
    private void UpdateHover(int x, int y)
    {
        if (PickColorMode) return;
        bool ctrl = CtrlDown;
        _lastCtrl = ctrl;
        var win = WindowFinder.At(_windows, x, y);
        if (win == null)
        {
            // desktop / no window: the monitor under the mouse
            var mon = MonitorAt(x, y);
            _hoverChain = mon != null ? new List<PxRect> { mon.Bounds } : new List<PxRect>();
            _hoverLevel = 0;
            _hoverHwnd = IntPtr.Zero;
            _hoverIsElement = false;
            return;
        }

        var winRect = Clip(win.Rect);
        if (!ctrl)
        {
            _elementCts?.Cancel();
            _hoverChain = new List<PxRect> { winRect };
            _hoverLevel = 0;
            _hoverHwnd = win.Hwnd;
            _hoverIsElement = false;
            return;
        }

        // Ctrl: keep the current element chain while the mouse stays inside its deepest rectangle
        bool stillInside = _hoverIsElement && _hoverHwnd == win.Hwnd && _hoverChain.Count > 1 && _hoverChain[^1].Contains(x, y);
        if (stillInside) return;
        if (!_hoverIsElement || _hoverHwnd != win.Hwnd)
        {
            _hoverChain = new List<PxRect> { winRect };
            _hoverLevel = 0;
        }
        _hoverHwnd = win.Hwnd;
        _hoverIsElement = true;
        QueryElement(win.Hwnd, x, y, winRect);
    }

    private async void QueryElement(IntPtr hwnd, int x, int y, PxRect winRect)
    {
        if (_elementBusy) return; // one query at a time; the next mouse move asks again
        _elementBusy = true;
        _elementCts?.Cancel();
        var cts = new CancellationTokenSource();
        _elementCts = cts;
        try
        {
            var chain = await WindowFinder.ElementChainAsync(hwnd, x, y, cts.Token);
            if (_finished || cts.IsCancellationRequested || _hoverHwnd != hwnd || !_hoverIsElement) return;
            var full = new List<PxRect> { winRect };
            foreach (var r in chain)
            {
                var c = Clip(r);
                if (!c.IsEmpty && c != full[^1]) full.Add(c);
            }
            _hoverChain = full;
            _hoverLevel = full.Count - 1;
            RefreshAll();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _elementBusy = false;
        }
        // the mouse may have moved on while we were busy
        if (!_finished && _hoverIsElement && Hover is PxRect h && !h.Contains(Cursor.X, Cursor.Y))
        {
            UpdateHover(Cursor.X, Cursor.Y);
            RefreshAll();
        }
    }

    /// <summary>Mouse wheel over a hovered element: up = parent (bigger), down = child (smaller).</summary>
    internal void Wheel(int delta)
    {
        if (_finished || HasSelection || _hoverChain.Count < 2) return;
        _hoverLevel = Math.Clamp(_hoverLevel + (delta > 0 ? -1 : 1), 0, _hoverChain.Count - 1);
        RefreshAll();
    }

    /// <summary>Pressing / releasing Ctrl without moving the mouse also switches window ↔ element.</summary>
    private void StartCtrlWatch()
    {
        _ctrlTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _ctrlTimer.Tick += (_, _) =>
        {
            if (_finished || HasSelection || Mode != DragMode.None) return;
            if (CtrlDown == _lastCtrl) return;
            UpdateHover(Cursor.X, Cursor.Y);
            RefreshAll();
        };
        _ctrlTimer.Start();
    }

    public void Show()
    {
        Cursor = Monitors.CursorPosition();
        foreach (var m in _monitors)
        {
            var w = new OverlayWindow(this, m, Shot);
            _overlays.Add(w);
            w.Show();
        }
        var target = _overlays.FirstOrDefault(o => o.Monitor.Bounds.Contains(Cursor.X, Cursor.Y)) ?? _overlays.FirstOrDefault();
        if (ToolsEnabled && !PickColorMode && Settings != null)
        {
            Editor = new CaptureEditor(this, Settings);
            Editor.TextInputChanged += on => TextInputChanged?.Invoke(on);
        }
        target?.FocusOverlay();
        UpdateHover(Cursor.X, Cursor.Y);
        StartCtrlWatch();
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (_finished) return;
        UpdateOwners();
        foreach (var o in _overlays) o.Render(this);
        Editor?.Sync();
    }

    /// <summary>Overlays only (e.g. the drawings changed while the edit window is hidden).</summary>
    public void RefreshOverlays()
    {
        if (_finished) return;
        foreach (var o in _overlays) o.Render(this);
    }

    private void UpdateOwners()
    {
        if (!HasSelection)
        {
            LabelMonitor = ActionMonitor = null;
            return;
        }
        LabelMonitor = MonitorAt(Selection.X, Selection.Y) ?? Largest();
        ActionMonitor = MonitorAt(Selection.Right - 1, Selection.Bottom - 1) ?? Largest();
    }

    internal bool AnyMonitorContains(int x, int y) => MonitorAt(x, y) != null;

    private MonitorInfo? MonitorAt(int x, int y) => _monitors.FirstOrDefault(m => m.Bounds.Contains(x, y));

    private MonitorInfo? Largest() => _monitors
        .OrderByDescending(m => { var i = m.Bounds.Intersect(Selection); return (long)i.Width * i.Height; })
        .FirstOrDefault();

    private (int, int) Clamp(int x, int y)
    {
        var b = Shot.Bounds;
        return (Math.Clamp(x, b.X, b.Right - 1), Math.Clamp(y, b.Y, b.Bottom - 1));
    }

    // ---------------- mouse ----------------

    internal void MouseDown(int x, int y, bool shift, int clickCount)
    {
        if (_finished) return;
        (x, y) = Clamp(x, y);
        Cursor = (x, y);

        if (PickColorMode)
        {
            PickedColor = Shot.GetPixel(x - Shot.OriginX, y - Shot.OriginY);
            PickedAsRgb = shift;
            Finish(CaptureOutcome.ColorPicked);
            return;
        }
        Editor?.OnOverlayMouseDown();

        if (clickCount >= 2 && HasSelection && Selection.Contains(x, y))
        {
            Finish(CaptureOutcome.Copy);
            return;
        }

        _startX = x;
        _startY = y;
        _startSel = Selection;
        var h = HitHandle(x, y);
        if (HasSelection && h != SelectionHandle.None)
        {
            Mode = DragMode.Resizing;
            _handle = h;
        }
        else if (HasSelection && Selection.Contains(x, y))
        {
            Mode = DragMode.Moving;
        }
        else
        {
            _clickHover = Hover;
            Mode = DragMode.Creating;
            Selection = new PxRect(x, y, 1, 1);
        }
        RefreshAll();
    }

    internal void MouseMove(int x, int y, bool shift)
    {
        if (_finished) return;
        (x, y) = Clamp(x, y);
        Cursor = (x, y);
        var b = Shot.Bounds;

        switch (Mode)
        {
            case DragMode.Creating:
            {
                int dx = x - _startX, dy = y - _startY;
                if (shift)
                {
                    int side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    dx = Math.Sign(dx == 0 ? 1 : dx) * side;
                    dy = Math.Sign(dy == 0 ? 1 : dy) * side;
                }
                int x2 = Math.Clamp(_startX + dx, b.X, b.Right - 1);
                int y2 = Math.Clamp(_startY + dy, b.Y, b.Bottom - 1);
                Selection = Inclusive(_startX, _startY, x2, y2);
                break;
            }
            case DragMode.Moving:
            {
                int nx = Math.Clamp(_startSel.X + (x - _startX), b.X, b.Right - _startSel.Width);
                int ny = Math.Clamp(_startSel.Y + (y - _startY), b.Y, b.Bottom - _startSel.Height);
                Selection = new PxRect(nx, ny, _startSel.Width, _startSel.Height);
                break;
            }
            case DragMode.Resizing:
                Selection = Resize(_startSel, _handle, x - _startX, y - _startY);
                break;
            default:
                UpdateCursorShape(x, y);
                if (!HasSelection) UpdateHover(x, y);
                break;
        }
        RefreshAll();
    }

    internal void MouseUp(int x, int y, bool shift)
    {
        if (_finished) return;
        if (Mode != DragMode.None) MouseMove(x, y, shift);
        if (Mode == DragMode.Creating && Selection.Width <= 3 && Selection.Height <= 3)
        {
            // a click, not a drag: select the highlighted window / element
            Mode = DragMode.None;
            Selection = default;
            if (_clickHover is PxRect h && !h.IsEmpty) Selection = h;
        }
        Mode = DragMode.None;
        _handle = SelectionHandle.None;
        UpdateCursorShape(Cursor.X, Cursor.Y);
        RefreshAll();
    }

    internal void MouseCaptureLost()
    {
        if (Mode != DragMode.None) MouseUp(Cursor.X, Cursor.Y, false);
    }

    internal void RightClick()
    {
        if (_finished) return;
        if (Mode != DragMode.None) return;
        if (HasSelection)
        {
            Selection = default;
            RefreshAll();
        }
        else
        {
            Finish(CaptureOutcome.Cancel);
        }
    }

    private static PxRect Inclusive(int x1, int y1, int x2, int y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1) + 1, Math.Abs(y2 - y1) + 1);

    private PxRect Resize(PxRect r, SelectionHandle h, int dx, int dy)
    {
        int l = r.X, t = r.Y, rt = r.Right, bt = r.Bottom;
        if (h is SelectionHandle.NW or SelectionHandle.W or SelectionHandle.SW) l += dx;
        if (h is SelectionHandle.NE or SelectionHandle.E or SelectionHandle.SE) rt += dx;
        if (h is SelectionHandle.NW or SelectionHandle.N or SelectionHandle.NE) t += dy;
        if (h is SelectionHandle.SW or SelectionHandle.S or SelectionHandle.SE) bt += dy;
        var b = Shot.Bounds;
        l = Math.Clamp(l, b.X, b.Right); rt = Math.Clamp(rt, b.X, b.Right);
        t = Math.Clamp(t, b.Y, b.Bottom); bt = Math.Clamp(bt, b.Y, b.Bottom);
        var n = PxRect.FromPoints(l, t, rt, bt);
        return new PxRect(n.X, n.Y, Math.Max(1, n.Width), Math.Max(1, n.Height));
    }

    internal SelectionHandle HitHandle(int x, int y)
    {
        if (!HasSelection) return SelectionHandle.None;
        var mon = MonitorAt(x, y);
        int tol = (int)Math.Ceiling(6 * (mon?.Scale ?? 1.0));
        var s = Selection;
        bool nearL = Math.Abs(x - s.X) <= tol, nearR = Math.Abs(x - s.Right) <= tol;
        bool nearT = Math.Abs(y - s.Y) <= tol, nearB = Math.Abs(y - s.Bottom) <= tol;
        bool inX = x >= s.X - tol && x <= s.Right + tol, inY = y >= s.Y - tol && y <= s.Bottom + tol;
        if (nearL && nearT) return SelectionHandle.NW;
        if (nearR && nearT) return SelectionHandle.NE;
        if (nearL && nearB) return SelectionHandle.SW;
        if (nearR && nearB) return SelectionHandle.SE;
        if (nearT && inX) return SelectionHandle.N;
        if (nearB && inX) return SelectionHandle.S;
        if (nearL && inY) return SelectionHandle.W;
        if (nearR && inY) return SelectionHandle.E;
        return SelectionHandle.None;
    }

    private void UpdateCursorShape(int x, int y)
    {
        System.Windows.Input.Cursor c = HitHandle(x, y) switch
        {
            SelectionHandle.NW or SelectionHandle.SE => Cursors.SizeNWSE,
            SelectionHandle.NE or SelectionHandle.SW => Cursors.SizeNESW,
            SelectionHandle.N or SelectionHandle.S => Cursors.SizeNS,
            SelectionHandle.E or SelectionHandle.W => Cursors.SizeWE,
            _ => HasSelection && Selection.Contains(x, y) ? Cursors.SizeAll : Cursors.Cross,
        };
        foreach (var o in _overlays) o.SetCursorShape(c);
    }

    // ---------------- keyboard (via global hook) ----------------

    public void HandleKey(int vk, HotkeyModifiers mods)
    {
        if (_finished) return;
        if (PickColorMode)
        {
            if (vk == 0x1B) Finish(CaptureOutcome.Cancel);
            return;
        }
        if (Editor != null)
        {
            HandleToolKey(vk, mods);
            return;
        }
        bool ctrl = mods.HasFlag(HotkeyModifiers.Ctrl), shift = mods.HasFlag(HotkeyModifiers.Shift);
        switch (vk)
        {
            case 0x1B: // Esc
                if (Mode != DragMode.None)
                {
                    Mode = DragMode.None;
                    Selection = _startSel;
                    RefreshAll();
                }
                else
                {
                    Finish(CaptureOutcome.Cancel);
                }
                break;
            case 0x0D: // Enter
                if (HasSelection) Finish(CaptureOutcome.Copy);
                break;
            case 0x43: // C
                if (ctrl && HasSelection) Finish(CaptureOutcome.Copy);
                break;
            case 0x53: // S
                if (ctrl && HasSelection) Finish(shift ? CaptureOutcome.SaveAs : CaptureOutcome.Save);
                break;
            case 0x45: // E
                if (HasSelection) Finish(CaptureOutcome.Edit);
                break;
            case 0x46: // F
                FullScreen(Monitors.FromCursor());
                break;
            case 0x50: // P
                if (HasSelection) Finish(CaptureOutcome.Pin);
                break;
            case 0x54: // T
                if (HasSelection) Finish(CaptureOutcome.Ocr);
                break;
            case 0x51: // Q
                if (HasSelection) Finish(CaptureOutcome.Qr);
                break;
            case 0x41: // A
                Finish(CaptureOutcome.AllScreens);
                break;
            case >= 0x25 and <= 0x28: // arrows: move selection, Shift = 10 px, Ctrl = resize
                if (HasSelection && Mode == DragMode.None) Nudge(vk, shift ? 10 : 1, ctrl);
                break;
        }
    }

    /// <summary>
    /// Keys with drawing tools on: before a frame exists F / A / Esc work as before; with a frame, letters pick
    /// tools (V B H L A R O T M N E U S, same letter again = no tool), P = pin, Q = QR, Ctrl+T = OCR,
    /// Ctrl+E = editor window, Ctrl+Z / Y = undo / redo, Delete = delete the selected drawing,
    /// arrows move the selected drawing (or the frame), Esc goes back one step at a time.
    /// </summary>
    private void HandleToolKey(int vk, HotkeyModifiers mods)
    {
        var ed = Editor!;
        var surf = ed.Surface;
        bool ctrl = mods.HasFlag(HotkeyModifiers.Ctrl), shift = mods.HasFlag(HotkeyModifiers.Shift);
        bool arrow = vk is >= 0x25 and <= 0x28;

        if (vk == 0x1B) // Esc
        {
            if (Mode != DragMode.None)
            {
                Mode = DragMode.None;
                Selection = _startSel;
                RefreshAll();
            }
            else if (!HasSelection || !ed.Escape()) ConfirmCancel();
            return;
        }
        if (!HasSelection)
        {
            if (ctrl) return;
            if (vk == 0x46) FullScreen(Monitors.FromCursor()); // F
            else if (vk == 0x41) Finish(CaptureOutcome.AllScreens); // A
            return;
        }
        if (ctrl)
        {
            switch (vk)
            {
                case 0x43: Finish(CaptureOutcome.Copy); break;                                       // Ctrl+C
                case 0x53: Finish(shift ? CaptureOutcome.SaveAs : CaptureOutcome.Save); break;       // Ctrl+S
                case 0x5A: if (shift) surf.DoRedo(); else surf.DoUndo(); break;                      // Ctrl+Z
                case 0x59: surf.DoRedo(); break;                                                     // Ctrl+Y
                case 0x54: Finish(CaptureOutcome.Ocr); break;                                        // Ctrl+T
                case 0x45: Finish(CaptureOutcome.Edit); break;                                       // Ctrl+E
                default:
                    if (arrow && Mode == DragMode.None) Nudge(vk, shift ? 10 : 1, resize: true);
                    break;
            }
            return;
        }
        switch (vk)
        {
            case 0x0D: Finish(CaptureOutcome.Copy); return;          // Enter
            case 0x2E or 0x08: surf.DeleteSelected(); return;         // Delete / Backspace
            case 0x50: Finish(CaptureOutcome.Pin); return;            // P
            case 0x51: Finish(CaptureOutcome.Qr); return;             // Q
        }
        if (arrow)
        {
            if (Mode != DragMode.None) return;
            if (surf.Selected != null)
            {
                var key = vk switch { 0x25 => Key.Left, 0x26 => Key.Up, 0x27 => Key.Right, _ => Key.Down };
                surf.NudgeSelected(key, shift ? 10 : 1);
            }
            else Nudge(vk, shift ? 10 : 1, resize: false);
            return;
        }
        if (EditSurface.ToolForKey(KeyInterop.KeyFromVirtualKey(vk)) is EditorTool tool) ed.ToggleTool(tool);
    }

    /// <summary>Cancel the capture; asks first when there are drawings.</summary>
    private void ConfirmCancel()
    {
        var ed = Editor;
        if (ed != null && ed.Surface.Items.Count > 0)
        {
            TextInputChanged?.Invoke(true); // the dialog needs Enter / Esc
            bool yes;
            try
            {
                yes = ConfirmDialog.Show(ed.DialogOwner, Strings.CaptureDiscard, Strings.CaptureDiscardYes, Strings.CaptureDiscardNo);
            }
            finally
            {
                TextInputChanged?.Invoke(false);
            }
            if (!yes) return;
        }
        Finish(CaptureOutcome.Cancel);
    }

    private void Nudge(int vk, int step, bool resize)
    {
        int dx = vk == 0x25 ? -step : vk == 0x27 ? step : 0;
        int dy = vk == 0x26 ? -step : vk == 0x28 ? step : 0;
        var s = Selection;
        var b = Shot.Bounds;
        if (resize)
        {
            int w = Math.Clamp(s.Width + dx, 1, b.Right - s.X);
            int h = Math.Clamp(s.Height + dy, 1, b.Bottom - s.Y);
            Selection = new PxRect(s.X, s.Y, w, h);
        }
        else
        {
            Selection = new PxRect(Math.Clamp(s.X + dx, b.X, b.Right - s.Width), Math.Clamp(s.Y + dy, b.Y, b.Bottom - s.Height), s.Width, s.Height);
        }
        RefreshAll();
    }

    // ---------------- finish ----------------

    private MonitorInfo? _fullScreenMonitor;

    public void FullScreen(MonitorInfo monitor)
    {
        _fullScreenMonitor = monitor;
        Finish(CaptureOutcome.FullScreen);
    }

    private bool HasDrawings => Editor != null && Editor.Surface.Items.Count > 0;

    /// <summary>A screen rectangle of the capture, with the drawings when there are any.</summary>
    private PixelImage Region(PxRect r) => HasDrawings ? Editor!.Render(r) : Shot.CropScreen(r);

    public void Finish(CaptureOutcome outcome)
    {
        if (_finished) return;
        _finished = true;

        PixelImage? result = null;
        try
        {
            switch (outcome)
            {
                case CaptureOutcome.Cancel:
                case CaptureOutcome.ColorPicked:
                    break;
                case CaptureOutcome.FullScreen:
                    result = Region((_fullScreenMonitor ?? Monitors.FromCursor()).Bounds);
                    break;
                case CaptureOutcome.AllScreens:
                    result = HasDrawings ? Region(Shot.Bounds) : Shot;
                    break;
                case CaptureOutcome.Edit when Editor != null:
                    if (!HasSelection) { outcome = CaptureOutcome.Cancel; break; }
                    // the editor window continues with the screenshot and the (still editable) drawings
                    result = Shot.CropScreen(Selection);
                    HandOverShift = new System.Windows.Vector(-(Selection.X - Shot.OriginX), -(Selection.Y - Shot.OriginY));
                    HandOverItems = Editor.DetachItems();
                    break;
                default:
                    if (!HasSelection) outcome = CaptureOutcome.Cancel;
                    else result = Region(Selection);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Crop failed", ex);
            outcome = CaptureOutcome.Cancel;
            result = null;
        }

        try { Editor?.Close(); } catch (Exception ex) { Logger.Error("Closing the capture editor failed", ex); }
        foreach (var o in _overlays)
        {
            try { o.Close(); } catch { /* ignore */ }
        }
        _overlays.Clear();
        _ctrlTimer?.Stop();
        _elementCts?.Cancel();
        Finished?.Invoke(outcome, result);
    }
}
