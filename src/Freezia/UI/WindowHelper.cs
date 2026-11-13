using System;
using System.Windows;
using System.Windows.Interop;
using Freezia.Capture;
using Freezia.Native;

namespace Freezia.UI;

public static class WindowHelper
{
    /// <summary>
    /// Title bar matching the current theme (dark/light + caption color) and rounded corners
    /// (Windows 10 2004+ / 11). Called again by <see cref="Core.ThemeManager"/> when the theme changes.
    /// </summary>
    public static void ApplyChrome(Window window)
    {
        void Apply()
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            try
            {
                bool dark = Core.ThemeManager.IsDark;
                int on = dark ? 1 : 0;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
                int corner = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                int caption = dark ? Rgb(0x20, 0x21, 0x27) : Rgb(0xFF, 0xFF, 0xFF); // = SurfaceBrush
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                int border = dark ? Rgb(0x3B, 0x3C, 0x46) : Rgb(0xD8, 0xDB, 0xE3);
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
                int text = dark ? Rgb(0xED, 0xED, 0xF2) : Rgb(0x1B, 0x1C, 0x22);
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
            catch
            {
                // older Windows: ignore
            }
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Apply();
        else window.SourceInitialized += (_, _) => Apply();
    }

    private static int Rgb(int r, int g, int b) => r | (g << 8) | (b << 16);

    /// <summary>Hide from Alt+Tab.</summary>
    public static void MakeToolWindow(Window window, bool noActivate = false)
    {
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        NativeMethods.AddExStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.RemoveExStyle(hwnd, NativeMethods.WS_EX_APPWINDOW);
        if (noActivate) NativeMethods.AddExStyle(hwnd, NativeMethods.WS_EX_NOACTIVATE);
    }

    /// <summary>Move/resize a window using physical pixels (bypasses WPF DIP conversion).</summary>
    public static void SetPhysicalBounds(Window window, PxRect r, bool topmost = true)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, topmost ? NativeMethods.HWND_TOPMOST : IntPtr.Zero, r.X, r.Y, r.Width, r.Height,
            NativeMethods.SWP_NOACTIVATE | (topmost ? 0 : NativeMethods.SWP_NOZORDER));
    }

    /// <summary>
    /// Center a window (Width/Height in DIPs) on the monitor under the cursor, clamped to its work area.
    /// Positioning is done in physical pixels once the HWND exists, so mixed-DPI setups work.
    /// Call before Show().
    /// </summary>
    public static void CenterOnCursorMonitor(Window window)
    {
        var mon = Monitors.FromCursor();
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        void Place(double scale)
        {
            var wa = mon.WorkArea;
            double dipW = double.IsNaN(window.Width) ? window.ActualWidth : window.Width;
            double dipH = double.IsNaN(window.Height) ? window.ActualHeight : window.Height;
            int w = (int)Math.Min(dipW * scale, wa.Width);
            int h = (int)Math.Min(dipH * scale, wa.Height);
            int x = wa.X + (wa.Width - w) / 2;
            int y = wa.Y + (wa.Height - h) / 2;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        }

        // Pass 1: move onto the target monitor (sized with the DPI the HWND was created with,
        // so a WM_DPICHANGED rescale lands on the right size).
        window.SourceInitialized += (_, _) =>
            Place(System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX);
        // Pass 2: re-center with the final DPI of the target monitor.
        window.Loaded += (_, _) => Place(mon.Scale);
    }
}
