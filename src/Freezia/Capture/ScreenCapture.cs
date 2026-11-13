using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Freezia.Capture;

/// <summary>
/// Screen capture in physical pixels. Uses Windows.Graphics.Capture per monitor when available
/// (games, hardware-accelerated content, HDR) and falls back to GDI automatically.
/// </summary>
public static class ScreenCapture
{
    /// <summary>Settings: "Auto" capture method (Windows.Graphics.Capture first).</summary>
    public static bool PreferWgc { get; set; } = true;

    /// <summary>Which method produced the last capture (for logs / tests).</summary>
    public static string LastMethod { get; private set; } = "GDI";

    public static PixelImage CaptureVirtualScreen() => CaptureRect(Monitors.VirtualScreen());

    public static PixelImage CaptureRect(PxRect r)
    {
        if (PreferWgc && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) && WgcCapture.IsSupported)
        {
            try
            {
                var img = CaptureWgc(r);
                LastMethod = "WGC";
                return img;
            }
            catch (Exception ex)
            {
                Core.Logger.Error("Windows.Graphics.Capture failed, using GDI", ex);
            }
        }
        LastMethod = "GDI";
        return CaptureGdi(r);
    }

    /// <summary>Compose the monitors that intersect <paramref name="r"/> (areas without a monitor stay black).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.18362.0")]
    private static PixelImage CaptureWgc(PxRect r)
    {
        var px = new byte[r.Width * r.Height * 4];
        for (int i = 3; i < px.Length; i += 4) px[i] = 255;
        foreach (var mon in Monitors.GetAll())
        {
            var part = mon.Bounds.Intersect(r);
            if (part.IsEmpty) continue;
            var img = WgcCapture.CaptureMonitor(mon);
            var from = part.Intersect(img.Bounds);
            for (int y = 0; y < from.Height; y++)
            {
                int si = (from.Y - img.OriginY + y) * img.Stride + (from.X - img.OriginX) * 4;
                int di = (from.Y - r.Y + y) * r.Width * 4 + (from.X - r.X) * 4;
                Buffer.BlockCopy(img.Pixels, si, px, di, from.Width * 4);
            }
        }
        return new PixelImage(r.Width, r.Height, px, r.X, r.Y);
    }

    public static PixelImage CaptureGdi(PxRect r)
    {
        if (r.IsEmpty) throw new InvalidOperationException("Empty capture rectangle");

        // Format32bppRgb: CopyFromScreen with ARGB can return alpha = 0 on some systems.
        using var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(r.X, r.Y, 0, 0, new Size(r.Width, r.Height), CopyPixelOperation.SourceCopy);
        }

        var data = bmp.LockBits(new Rectangle(0, 0, r.Width, r.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            int rowBytes = r.Width * 4;
            var pixels = new byte[rowBytes * r.Height];
            for (int y = 0; y < r.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * rowBytes, rowBytes);
            // Force opaque alpha so Pbgra32 is valid.
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return new PixelImage(r.Width, r.Height, pixels, r.X, r.Y);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
