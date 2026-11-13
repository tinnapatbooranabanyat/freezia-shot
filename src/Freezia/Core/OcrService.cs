using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Freezia.Capture;
using Freezia.Native;
using TesseractOCR;
using TesseractOCR.Enums;

namespace Freezia.Core;

/// <summary>
/// Thai + English OCR with Tesseract, fully offline. The native DLLs and language models are embedded
/// in Freezia.exe and extracted once to %LocalAppData%\Freezia\ocr\&lt;version&gt;\ on first use.
/// Needs the Microsoft Visual C++ 2015-2022 runtime (the installer adds it when missing).
/// </summary>
public static class OcrService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Engine? _engine;

    public sealed class MissingRuntimeException : Exception
    {
        public MissingRuntimeException(Exception inner) : base(inner.Message, inner) { }
    }

    private static string RootDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "ocr", AppInfo.Version);

    /// <summary>Recognize text in an image (runs on a worker thread).</summary>
    public static Task<string> RecognizeAsync(PixelImage image) => Task.Run(async () =>
    {
        await Gate.WaitAsync();
        try
        {
            var engine = EnsureEngine();
            byte[] png = ToPng(Prepare(image));
            using var pix = TesseractOCR.Pix.Image.LoadFromMemory(png);
            using var page = engine.Process(pix);
            return Clean(page.Text ?? "");
        }
        finally
        {
            Gate.Release();
        }
    });

    private static Engine EnsureEngine()
    {
        if (_engine != null) return _engine;
        string dir = RootDir;
        Extract(dir);
        TesseractOCR.InteropDotNet.LibraryLoader.Instance.CustomSearchPath = dir;
        // so tesseract55.dll finds leptonica next to it
        NativeMethods.SetDllDirectory(Path.Combine(dir, "x64"));
        try
        {
            _engine = new Engine(Path.Combine(dir, "tessdata"), new List<Language> { Language.Thai, Language.English }, EngineMode.Default);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException
                                   || ex.GetType().Name.Contains("LoadLibrary") || ex.InnerException is DllNotFoundException)
        {
            Logger.Error("OCR engine failed to load (VC++ runtime missing?)", ex);
            throw new MissingRuntimeException(ex);
        }
        return _engine;
    }

    /// <summary>Copy the embedded natives and models out of the exe (once per app version).</summary>
    private static void Extract(string dir)
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith("ocr/", StringComparison.Ordinal)) continue;
            string target = Path.Combine(dir, name.Substring(4).Replace('/', Path.DirectorySeparatorChar));
            using var src = asm.GetManifestResourceStream(name)!;
            if (File.Exists(target) && new FileInfo(target).Length == src.Length) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string tmp = target + ".tmp";
            using (var fs = File.Create(tmp)) src.CopyTo(fs);
            File.Move(tmp, target, overwrite: true);
        }
        // remove models of older app versions
        try
        {
            foreach (var old in Directory.GetDirectories(Path.GetDirectoryName(dir)!))
                if (!string.Equals(old, dir, StringComparison.OrdinalIgnoreCase)) Directory.Delete(old, true);
        }
        catch
        {
            // in use by another running copy: leave it
        }
    }

    /// <summary>Small screenshots read much better when enlarged (Tesseract likes ~30 px text).</summary>
    private static BitmapSource Prepare(PixelImage image)
    {
        BitmapSource src = image.Source;
        double scale = image.Height < 120 || image.Width < 400 ? 3 : image.Height < 400 ? 2 : 1;
        if (scale > 1 && image.Width * scale < 10000 && image.Height * scale < 10000)
        {
            var t = new TransformedBitmap(src, new ScaleTransform(scale, scale));
            t.Freeze();
            src = t;
        }
        return src;
    }

    private static byte[] ToPng(BitmapSource bmp)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static readonly Regex BlankLines = new(@"\n{3,}", RegexOptions.Compiled);

    /// <summary>Normalize line breaks. Tesseract's Thai model already keeps the real spaces between Thai words.</summary>
    public static string Clean(string text)
    {
        text = text.Replace("\r\n", "\n").Trim();
        text = BlankLines.Replace(text, "\n\n");
        return text.Replace("\n", Environment.NewLine);
    }
}
