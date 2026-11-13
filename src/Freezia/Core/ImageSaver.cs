using System;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Freezia.Core;

public static class ImageSaver
{
    public static string Extension(ImageFormatKind f) => f == ImageFormatKind.Jpg ? ".jpg" : ".png";

    /// <summary>Screenshot_yyyy-MM-dd_HH-mm-ss (always Gregorian, even on a Thai-calendar system).</summary>
    public static string BaseName(DateTime t) =>
        "Screenshot_" + t.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);

    /// <summary>A file name in <paramref name="folder"/> that does not exist yet ("name (2).png"...).</summary>
    public static string UniquePath(string folder, string baseName, string ext)
    {
        string path = Path.Combine(folder, baseName + ext);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{baseName} ({i}){ext}");
        return path;
    }

    /// <summary>Save into the configured folder with an automatic, non-overwriting name.</summary>
    public static string SaveAuto(BitmapSource image, AppSettings s)
    {
        string folder = KnownFolders.ResolveSaveFolder(s);
        string ext = Extension(s.Format);
        string baseName = BaseName(DateTime.Now);
        for (int attempt = 0; attempt < 50; attempt++)
        {
            string path = UniquePath(folder, baseName, ext);
            try
            {
                // CreateNew: never overwrite, even if another file appeared in the meantime.
                using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                Encode(image, s.Format, s.JpegQuality, fs);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                // race: try the next number
            }
        }
        throw new IOException("Could not find a free file name");
    }

    /// <summary>Save to an explicit path (Save As). Format follows the extension.</summary>
    public static void SaveTo(BitmapSource image, string path, int jpegQuality)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        var fmt = ext is ".jpg" or ".jpeg" ? ImageFormatKind.Jpg : ImageFormatKind.Png;
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
                Encode(image, fmt, jpegQuality, fs);
            File.Move(tmp, path, overwrite: true); // user explicitly confirmed the name in the dialog
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    public static void Encode(BitmapSource image, ImageFormatKind format, int jpegQuality, Stream stream)
    {
        BitmapEncoder enc;
        BitmapSource src = image;
        if (format == ImageFormatKind.Jpg)
        {
            // JPEG has no alpha: flatten to 24-bit.
            src = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
            enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) };
        }
        else
        {
            enc = new PngBitmapEncoder();
        }
        enc.Frames.Add(BitmapFrame.Create(src));
        enc.Save(stream);
    }
}
