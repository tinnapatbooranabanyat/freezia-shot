using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapEdit.Core;

public static class ClipboardHelper
{
    /// <summary>
    /// Put an image on the clipboard as both a bitmap (CF_BITMAP/CF_DIB) and a "PNG" stream
    /// (better for chat apps / Office). Retries while the clipboard is busy.
    /// </summary>
    public static bool SetImage(BitmapSource image)
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
