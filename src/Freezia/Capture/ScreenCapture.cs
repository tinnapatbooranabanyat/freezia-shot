using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Freezia.Capture;

/// <summary>GDI screen capture of the whole virtual screen (all monitors) in physical pixels.</summary>
public static class ScreenCapture
{
    public static PixelImage CaptureVirtualScreen() => CaptureRect(Monitors.VirtualScreen());

    public static PixelImage CaptureRect(PxRect r)
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
