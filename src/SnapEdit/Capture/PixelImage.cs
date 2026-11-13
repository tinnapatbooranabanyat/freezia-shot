using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapEdit.Capture;

/// <summary>
/// An opaque 32-bit (Pbgra32, alpha = 255) image kept as raw bytes, so pixel colors can be read
/// quickly (loupe, eyedropper, mosaic). <see cref="OriginX"/>/<see cref="OriginY"/> are the physical
/// screen coordinates of the top-left pixel.
/// </summary>
public sealed class PixelImage
{
    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;
    public byte[] Pixels { get; }
    public int OriginX { get; }
    public int OriginY { get; }

    private BitmapSource? _source;

    public PixelImage(int width, int height, byte[] pixels, int originX = 0, int originY = 0)
    {
        if (pixels.Length < width * height * 4) throw new ArgumentException("pixel buffer too small");
        Width = width;
        Height = height;
        Pixels = pixels;
        OriginX = originX;
        OriginY = originY;
    }

    public PxRect Bounds => new(OriginX, OriginY, Width, Height);

    /// <summary>Frozen 96-DPI bitmap (1 DIP = 1 pixel).</summary>
    public BitmapSource Source
    {
        get
        {
            if (_source == null)
            {
                var bmp = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Pbgra32, null, Pixels, Stride);
                bmp.Freeze();
                _source = bmp;
            }
            return _source;
        }
    }

    public Color GetPixel(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        int i = y * Stride + x * 4;
        return Color.FromRgb(Pixels[i + 2], Pixels[i + 1], Pixels[i]);
    }

    /// <summary>Crop using physical screen coordinates.</summary>
    public PixelImage CropScreen(PxRect screenRect) =>
        Crop(screenRect.Offset(-OriginX, -OriginY));

    /// <summary>Crop using image-relative coordinates (clamped to the image).</summary>
    public PixelImage Crop(PxRect r)
    {
        r = r.Intersect(new PxRect(0, 0, Width, Height));
        if (r.IsEmpty) throw new ArgumentException("Empty crop");
        var dst = new byte[r.Width * r.Height * 4];
        int rowBytes = r.Width * 4;
        for (int y = 0; y < r.Height; y++)
            Buffer.BlockCopy(Pixels, (r.Y + y) * Stride + r.X * 4, dst, y * rowBytes, rowBytes);
        return new PixelImage(r.Width, r.Height, dst, OriginX + r.X, OriginY + r.Y);
    }

    /// <summary>Copy any BitmapSource into an opaque PixelImage.</summary>
    public static PixelImage FromBitmapSource(BitmapSource src)
    {
        BitmapSource s = src.Format == PixelFormats.Pbgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
        int w = s.PixelWidth, h = s.PixelHeight;
        var px = new byte[w * h * 4];
        s.CopyPixels(px, w * 4, 0);
        return new PixelImage(w, h, px);
    }
}
