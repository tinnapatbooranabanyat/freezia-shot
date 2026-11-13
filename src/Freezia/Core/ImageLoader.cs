using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Freezia.Capture;

namespace Freezia.Core;

/// <summary>Loads image files / clipboard images for the editor (not only screen captures).</summary>
public static class ImageLoader
{
    /// <summary>Formats Windows can decode out of the box (WebP/HEIC/AVIF need the Store codecs).</summary>
    public static readonly string[] Extensions =
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico", ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp",
    };

    public const int MaxSide = 20000;

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static string DialogFilter =>
        $"{Strings.ImageFiles}|{string.Join(";", Extensions.Select(e => "*" + e))}|{Strings.AllFiles}|*.*";

    /// <summary>Decode the first frame, apply the EXIF orientation and flatten transparency onto white.</summary>
    public static PixelImage Load(string path)
    {
        BitmapFrame frame;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
        }
        BitmapSource src = ApplyOrientation(frame, ReadOrientation(frame));
        return FromSource(src);
    }

    /// <summary>Image from the clipboard (screenshots from other apps, copied images); null if there is none.</summary>
    public static PixelImage? FromClipboard()
    {
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource bmp) return FromSource(bmp);
            if (Clipboard.ContainsFileDropList())
            {
                foreach (string? f in Clipboard.GetFileDropList())
                    if (f != null && IsSupported(f) && File.Exists(f)) return Load(f);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Reading the clipboard image failed", ex);
        }
        return null;
    }

    public static PixelImage FromSource(BitmapSource src)
    {
        if (src.PixelWidth > MaxSide || src.PixelHeight > MaxSide)
            throw new InvalidOperationException(string.Format(Strings.ImageTooLarge, src.PixelWidth, src.PixelHeight, MaxSide));

        // Flatten onto white (the editor and JPG export work with opaque images) at 96 DPI so 1 px = 1 DIP.
        int w = src.PixelWidth, h = src.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            dc.DrawImage(src, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return PixelImage.FromBitmapSource(rtb);
    }

    private static int ReadOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata md && md.ContainsQuery("System.Photo.Orientation") &&
                md.GetQuery("System.Photo.Orientation") is ushort o)
                return o;
        }
        catch
        {
            // formats without this metadata
        }
        return 1;
    }

    /// <summary>EXIF orientation 1-8 (phone photos are stored sideways with a rotation tag).</summary>
    public static BitmapSource ApplyOrientation(BitmapSource src, int orientation)
    {
        Transform? t = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
            8 => new RotateTransform(270),
            _ => null,
        };
        if (t == null) return src;
        var tb = new TransformedBitmap(src, t);
        tb.Freeze();
        return tb;
    }
}
