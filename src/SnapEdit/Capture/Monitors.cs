using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SnapEdit.Native;

namespace SnapEdit.Capture;

/// <summary>Integer rectangle in physical (device) pixels.</summary>
public readonly record struct PxRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int px, int py) => px >= X && px < Right && py >= Y && py < Bottom;

    public PxRect Intersect(PxRect o)
    {
        int x1 = Math.Max(X, o.X), y1 = Math.Max(Y, o.Y);
        int x2 = Math.Min(Right, o.Right), y2 = Math.Min(Bottom, o.Bottom);
        return x2 > x1 && y2 > y1 ? new PxRect(x1, y1, x2 - x1, y2 - y1) : default;
    }

    public PxRect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    public static PxRect FromPoints(int x1, int y1, int x2, int y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    internal static PxRect From(NativeMethods.RECT r) => new(r.Left, r.Top, r.Width, r.Height);
}

public sealed class MonitorInfo
{
    public required IntPtr Handle { get; init; }
    public required PxRect Bounds { get; init; }
    public required PxRect WorkArea { get; init; }
    public required bool IsPrimary { get; init; }
    public required string DeviceName { get; init; }
    /// <summary>Scale factor (1.0 = 96 DPI).</summary>
    public required double Scale { get; init; }
}

public static class Monitors
{
    public static List<MonitorInfo> GetAll()
    {
        var list = new List<MonitorInfo>();
        NativeMethods.MonitorEnumProc proc = (IntPtr h, IntPtr hdc, ref NativeMethods.RECT r, IntPtr data) =>
        {
            var info = Get(h);
            if (info != null) list.Add(info);
            return true;
        };
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        GC.KeepAlive(proc);
        // primary first, then left-to-right
        return list.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
    }

    private static MonitorInfo? Get(IntPtr h)
    {
        var mi = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (!NativeMethods.GetMonitorInfo(h, ref mi)) return null;
        double scale = 1.0;
        try
        {
            if (NativeMethods.GetDpiForMonitor(h, 0 /* MDT_EFFECTIVE_DPI */, out uint dx, out _) == 0 && dx > 0)
                scale = dx / 96.0;
        }
        catch
        {
            // shcore not available: assume 96 DPI
        }
        return new MonitorInfo
        {
            Handle = h,
            Bounds = PxRect.From(mi.rcMonitor),
            WorkArea = PxRect.From(mi.rcWork),
            IsPrimary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
            DeviceName = mi.szDevice ?? "",
            Scale = scale,
        };
    }

    public static (int X, int Y) CursorPosition()
    {
        NativeMethods.GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    /// <summary>The monitor under the mouse cursor (nearest if none).</summary>
    public static MonitorInfo FromCursor()
    {
        var (x, y) = CursorPosition();
        return FromPoint(x, y);
    }

    public static MonitorInfo FromPoint(int x, int y)
    {
        IntPtr h = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = x, Y = y }, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return Get(h) ?? GetAll().First();
    }

    /// <summary>Bounding rectangle of all monitors (the virtual screen).</summary>
    public static PxRect VirtualScreen() => new(
        NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
}
