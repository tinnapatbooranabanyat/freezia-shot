using System;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Freezia.Core;

public static class ClipboardHelper
{
    /// <summary>
    /// Put an image on the clipboard as both a bitmap (CF_BITMAP/CF_DIB) and a "PNG" stream
    /// (better for chat apps / Office). Retries while the clipboard is busy.
    /// </summary>
    /// <summary>Temp copies used for the clipboard file (removed on uninstall with %LocalAppData%\Freezia).</summary>
    public static string ClipDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "clipboard");

    /// <summary>
    /// Also put the image on the clipboard as a file named like a saved screenshot, so pasting into
    /// Explorer, chat apps or a browser upload gets "Screenshot_yyyy-MM-dd_HH-mm-ss.png" instead of "image.png".
    /// Returns the file name used, or null.
    /// </summary>
    public static string? SetImageWithFile(BitmapSource image, out bool ok)
    {
        string? path = null;
        try
        {
            Directory.CreateDirectory(ClipDir);
            PruneClipDir();
            path = ImageSaver.UniquePath(ClipDir, ImageSaver.BaseName(DateTime.Now), ".png");
            using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                ImageSaver.Encode(image, ImageFormatKind.Png, 100, fs);
        }
        catch (Exception ex)
        {
            Logger.Error("Clipboard temp file failed", ex);
            path = null;
        }
        ok = SetImage(image, path);
        return path == null ? null : Path.GetFileName(path);
    }

    /// <summary>Keep the temp folder small: drop files older than a day, keep at most 30.</summary>
    private static void PruneClipDir()
    {
        try
        {
            var files = new DirectoryInfo(ClipDir).GetFiles("*.png");
            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (int i = 0; i < files.Length; i++)
                if (i >= 30 || files[i].LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1))
                    files[i].Delete();
        }
        catch
        {
            // a file still in use by a paste target: try again next time
        }
    }

    public static bool SetImage(BitmapSource image) => SetImage(image, null);

    private static bool SetImage(BitmapSource image, string? filePath)
    {
        // Opaque 32-bit without alpha avoids black/transparent backgrounds in some apps.
        BitmapSource bmp = image.Format == PixelFormats.Bgr32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
        if (!bmp.IsFrozen && bmp.CanFreeze) bmp.Freeze();

        var png = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(image));
        enc.Save(png);
        png.Position = 0;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var data = new DataObject();
                data.SetImage(bmp);
                data.SetData("PNG", png, false);
                if (filePath != null)
                {
                    data.SetFileDropList(new StringCollection { filePath });
                    data.SetData("FileNameW", filePath);
                }
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (COMException ex)
            {
                if (attempt == 9) Logger.Error("Clipboard busy", ex);
            }
            catch (ExternalException ex)
            {
                if (attempt == 9) Logger.Error("Clipboard error", ex);
            }
            Thread.Sleep(60);
            png.Position = 0;
        }
        return false;
    }
}
