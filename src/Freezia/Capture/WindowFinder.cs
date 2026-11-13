using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using Freezia.Core;
using Freezia.Native;

namespace Freezia.Capture;

/// <summary>A visible top-level window, in z-order (topmost first).</summary>
public sealed record WindowRegion(IntPtr Hwnd, PxRect Rect);

/// <summary>
/// Finds windows and UI elements under the mouse for click-to-select in the capture overlay.
/// The window list is snapshotted BEFORE the overlay appears (afterwards the overlay would be on top).
/// </summary>
public static class WindowFinder
{
    private static readonly HashSet<string> SkipClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW",                 // desktop (falls back to the whole monitor instead)
        "Windows.UI.Core.CoreWindow",         // hidden shell surfaces
    };

    public static List<WindowRegion> Snapshot()
    {
        var list = new List<WindowRegion>();
        int myPid = Environment.ProcessId;
        var cls = new StringBuilder(256);
        NativeMethods.EnumWindowsProc proc = (h, _) =>
        {
            try
            {
                if (!NativeMethods.IsWindowVisible(h) || NativeMethods.IsIconic(h)) return true;
                if (NativeMethods.DwmGetWindowAttribute(h, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;
                cls.Clear();
                NativeMethods.GetClassName(h, cls, cls.Capacity);
                if (SkipClasses.Contains(cls.ToString())) return true;

                // our own toasts / tray menu are tool windows: skip them, keep editor & settings windows
                NativeMethods.GetWindowThreadProcessId(h, out uint pid);
                if (pid == (uint)myPid && (NativeMethods.GetExStyle(h).ToInt64() & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

                // extended frame bounds = the visible window without the invisible resize border
                NativeMethods.RECT r;
                if (NativeMethods.DwmGetWindowAttribute(h, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out r, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0)
                    NativeMethods.GetWindowRect(h, out r);
                var rect = PxRect.From(r);
                if (rect.Width < 8 || rect.Height < 8) return true;
                list.Add(new WindowRegion(h, rect));
            }
            catch
            {
                // a window vanished while enumerating: ignore it
            }
            return true;
        };
        NativeMethods.EnumWindows(proc, IntPtr.Zero);
        GC.KeepAlive(proc);
        return list;
    }

    public static WindowRegion? At(IReadOnlyList<WindowRegion> windows, int x, int y)
    {
        foreach (var w in windows)
            if (w.Rect.Contains(x, y)) return w;
        return null;
    }

    /// <summary>
    /// UI Automation: rectangles from the window down to the smallest element containing the point
    /// (outermost first). Runs off the UI thread; slow or broken providers are cut off by a timeout.
    /// </summary>
    public static Task<List<PxRect>> ElementChainAsync(IntPtr hwnd, int x, int y, CancellationToken ct) => Task.Run(() =>
    {
        var chain = new List<PxRect>();
        var sw = Stopwatch.StartNew();
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var walker = TreeWalker.ControlViewWalker;
            var current = root;
            var pt = new System.Windows.Point(x, y);
            for (int depth = 0; depth < 40 && !ct.IsCancellationRequested && sw.ElapsedMilliseconds < 1500; depth++)
            {
                AutomationElement? best = null;
                double bestArea = double.MaxValue;
                var child = walker.GetFirstChild(current);
                for (int n = 0; child != null && n < 400; n++)
                {
                    try
                    {
                        var r = child.Current.BoundingRectangle;
                        if (!r.IsEmpty && r.Width >= 2 && r.Height >= 2 && r.Contains(pt) && r.Width * r.Height < bestArea)
                        {
                            best = child;
                            bestArea = r.Width * r.Height;
                        }
                    }
                    catch (ElementNotAvailableException)
                    {
                    }
                    child = walker.GetNextSibling(child);
                }
                if (best == null) break;
                var br = best.Current.BoundingRectangle;
                chain.Add(new PxRect((int)Math.Round(br.X), (int)Math.Round(br.Y), (int)Math.Round(br.Width), (int)Math.Round(br.Height)));
                current = best;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Logger.Info("UI Automation lookup failed: " + ex.Message);
        }
        return chain;
    }, ct);
}
