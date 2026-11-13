using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Freezia.Core;
using Freezia.Native;
using Freezia.UI;

namespace Freezia.Capture;

/// <summary>Starts captures and performs the resulting copy / save / save-as / edit actions.</summary>
public sealed class CaptureController
{
    private readonly SettingsService _settings;
    private readonly HotkeyManager _hotkeys;
    private CaptureSession? _session;

    /// <summary>Opens the editor for an image (set by the app once the editor exists).</summary>
    public Action<PixelImage>? OpenEditor { get; set; }

    public CaptureController(SettingsService settings, HotkeyManager hotkeys)
    {
        _settings = settings;
        _hotkeys = hotkeys;
    }

    public bool IsBusy => _session != null;

    // ---------------- entry points ----------------

    public void CaptureRegion()
    {
        if (IsBusy) return;
        PixelImage shot;
        System.Collections.Generic.List<WindowRegion> windows;
        try
        {
            windows = WindowFinder.Snapshot(); // before the overlay covers everything
            shot = ScreenCapture.CaptureVirtualScreen();
        }
        catch (Exception ex)
        {
            Logger.Error("Capture failed", ex);
            ToastWindow.ShowToast(Strings.ToastCaptureFailed, ex.Message, error: true);
            return;
        }

        var session = new CaptureSession(shot, Monitors.GetAll(), windows);
        _session = session;
        session.Finished += OnSessionFinished;
        _hotkeys.SetCaptureHandler(session.HandleKey);
        try
        {
            session.Show();
        }
        catch (Exception ex)
        {
            Logger.Error("Overlay failed", ex);
            session.Finish(CaptureOutcome.Cancel);
        }
    }

    public void CaptureScreen()
    {
        if (IsBusy) return;
        try
        {
            var mon = Monitors.FromCursor();
            var img = ScreenCapture.CaptureRect(mon.Bounds);
            Remember(img.Source);
            HandleFullCapture(img);
        }
        catch (Exception ex)
        {
            Logger.Error("Screen capture failed", ex);
            ToastWindow.ShowToast(Strings.ToastCaptureFailed, ex.Message, error: true);
        }
    }

    public void CaptureAllScreens()
    {
        if (IsBusy) return;
        try
        {
            var img = ScreenCapture.CaptureVirtualScreen();
            Remember(img.Source);
            HandleFullCapture(img);
        }
        catch (Exception ex)
        {
            Logger.Error("All-screens capture failed", ex);
            ToastWindow.ShowToast(Strings.ToastCaptureFailed, ex.Message, error: true);
        }
    }

    // ---------------- results ----------------

    private void OnSessionFinished(CaptureOutcome outcome, PixelImage? image)
    {
        var session = _session;
        _hotkeys.SetCaptureHandler(null);
        _session = null;
        if (outcome == CaptureOutcome.ColorPicked && session != null)
        {
            CopyColor(session.PickedColor, session.PickedAsRgb);
            return;
        }
        if (image == null) return;
        Remember(image.Source);

        switch (outcome)
        {
            case CaptureOutcome.Copy: Copy(image.Source); break;
            case CaptureOutcome.Save: Save(image.Source); break;
            case CaptureOutcome.SaveAs: SaveAs(image.Source, null); break;
            case CaptureOutcome.Edit: Edit(image); break;
            case CaptureOutcome.FullScreen:
            case CaptureOutcome.AllScreens:
                HandleFullCapture(image);
                break;
            case CaptureOutcome.Pin: Pin(image, atCapturePosition: true); break;
            case CaptureOutcome.Ocr: Ocr(image); break;
            case CaptureOutcome.Qr: ReadCodes(image); break;
        }
    }

    /// <summary>Full screen / all screens: action chosen in settings.</summary>
    private void HandleFullCapture(PixelImage image)
    {
        switch (_settings.Current.AfterFullCapture)
        {
            case AfterCaptureAction.Copy: Copy(image.Source); break;
            case AfterCaptureAction.Save: Save(image.Source); break;
            default: Edit(image); break;
        }
    }

    private void Edit(PixelImage image)
    {
        if (OpenEditor != null) OpenEditor(image);
        else Copy(image.Source);
    }

    public bool Copy(BitmapSource image)
    {
        string? name = ClipboardHelper.SetImageWithFile(image, out bool ok);
        if (ok)
        {
            string size = $"{image.PixelWidth} × {image.PixelHeight} px";
            ToastWindow.ShowToast(Strings.ToastCopied, name == null ? size : $"{name} • {size}",
                revealPath: SafeSaveFolder(), thumb: image);
            return true;
        }
        ToastWindow.ShowToast(Strings.ToastCopyFailed, error: true);
        return false;
    }

    public string? Save(BitmapSource image)
    {
        try
        {
            string path = ImageSaver.SaveAuto(image, _settings.Current);
            ToastWindow.ShowToast(Strings.ToastSaved, Path.GetFileName(path) + " • " + Strings.ToastClickToOpen,
                revealPath: path, thumb: image);
            return path;
        }
        catch (Exception ex)
        {
            Logger.Error("Save failed", ex);
            ToastWindow.ShowToast(Strings.ToastSaveFailed, ex.Message, error: true);
            return null;
        }
    }

    /// <summary>Shows a Save As dialog. <paramref name="owner"/> may be null (a temporary owner is used).</summary>
    public string? SaveAs(BitmapSource image, Window? owner)
    {
        var s = _settings.Current;
        bool jpg = s.Format == ImageFormatKind.Jpg;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.SaveDialogTitle,
            Filter = Strings.FileFilter,
            FilterIndex = jpg ? 2 : 1,
            DefaultExt = ImageSaver.Extension(s.Format),
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = SafeSaveFolder(),
            FileName = ImageSaver.BaseName(DateTime.Now) + ImageSaver.Extension(s.Format),
        };

        Window? host = null;
        bool? ok;
        bool wasSuspended = _hotkeys.Suspended;
        _hotkeys.Suspended = true;
        try
        {
            if (owner == null)
            {
                host = CreateDialogHost();
                owner = host;
            }
            ok = dlg.ShowDialog(owner);
        }
        finally
        {
            _hotkeys.Suspended = wasSuspended;
            host?.Close();
        }
        if (ok != true) return null;

        try
        {
            ImageSaver.SaveTo(image, dlg.FileName, s.JpegQuality);
            ToastWindow.ShowToast(Strings.ToastSaved, Path.GetFileName(dlg.FileName) + " • " + Strings.ToastClickToOpen,
                revealPath: dlg.FileName, thumb: image);
            return dlg.FileName;
        }
        catch (Exception ex)
        {
            Logger.Error("Save As failed", ex);
            ToastWindow.ShowToast(Strings.ToastSaveFailed, ex.Message, error: true);
            return null;
        }
    }

    // ---------------- history + screen color picker ----------------

    /// <summary>Add to the capture history (no-op when history is turned off).</summary>
    public void Remember(BitmapSource image) => HistoryService.Add(image, _settings.Current);

    /// <summary>Freeze the screen and let the user click any pixel to copy its color.</summary>
    public void PickColor()
    {
        if (IsBusy) return;
        PixelImage shot;
        try
        {
            shot = ScreenCapture.CaptureVirtualScreen();
        }
        catch (Exception ex)
        {
            Logger.Error("Capture failed", ex);
            ToastWindow.ShowToast(Strings.ToastCaptureFailed, ex.Message, error: true);
            return;
        }
        var session = new CaptureSession(shot, Monitors.GetAll()) { PickColorMode = true };
        _session = session;
        session.Finished += OnSessionFinished;
        _hotkeys.SetCaptureHandler(session.HandleKey);
        session.Show();
    }

    private void CopyColor(System.Windows.Media.Color c, bool asRgb)
    {
        string text = asRgb ? $"rgb({c.R}, {c.G}, {c.B})" : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        for (int i = 0; i < 10; i++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                break;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                System.Threading.Thread.Sleep(50);
            }
        }
        // remember it in the editor's recent colors
        var recent = _settings.Current.Editor.RecentColors;
        string hex8 = $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";
        recent.RemoveAll(x => string.Equals(x, hex8, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, hex8);
        if (recent.Count > 8) recent.RemoveRange(8, recent.Count - 8);
        _settings.Save();

        var swatch = BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, new byte[] { c.B, c.G, c.R, 0 }, 4);
        swatch.Freeze();
        ToastWindow.ShowToast(Strings.ColorCopied, text, thumb: swatch);
    }

    // ---------------- pin / OCR / QR ----------------

    /// <summary>Float an image above all windows (at the place it was captured, or centered).</summary>
    public void Pin(PixelImage image, bool atCapturePosition)
    {
        var pin = new PinWindow(image, img => Edit(img), img => Copy(img.Source), img => Save(img.Source));
        if (atCapturePosition) pin.ShowAt(image.OriginX - 1, image.OriginY - 1);
        else pin.ShowCentered();
    }

    public async void Ocr(PixelImage image)
    {
        var win = new TextResultWindow(Strings.OcrTitle, image.Source);
        win.ShowBusy(Strings.OcrBusy);
        win.ShowAndActivate();
        try
        {
            string text = await OcrService.RecognizeAsync(image);
            if (text.Length == 0) win.ShowResult(Strings.OcrNoText, "");
            else
            {
                win.ShowResult(string.Format(Strings.OcrDone, text.Length), text);
                ToastWindow.ShowToast(Strings.TextCopied);
            }
        }
        catch (OcrService.MissingRuntimeException)
        {
            win.ShowError(Strings.OcrMissingRuntime);
        }
        catch (Exception ex)
        {
            Logger.Error("OCR failed", ex);
            win.ShowError(Strings.OcrFailed + "\n" + ex.Message);
        }
    }

    public async void ReadCodes(PixelImage image)
    {
        var win = new TextResultWindow(Strings.QrTitle, image.Source);
        win.ShowBusy(Strings.QrBusy);
        win.ShowAndActivate();
        try
        {
            var codes = await QrService.ReadAsync(image);
            if (codes.Count == 0)
            {
                win.ShowResult(Strings.QrNone, "");
                return;
            }
            string text = string.Join(Environment.NewLine + Environment.NewLine, codes.Select(c => c.Text));
            string? link = codes.Select(c => c.Text.Trim()).FirstOrDefault(QrService.IsLink);
            string formats = string.Join(", ", codes.Select(c => c.Format).Distinct());
            win.ShowResult(string.Format(Strings.QrFound, codes.Count, formats), text, link);
            ToastWindow.ShowToast(Strings.TextCopied);
        }
        catch (Exception ex)
        {
            Logger.Error("QR read failed", ex);
            win.ShowError(Strings.QrFailed + "\n" + ex.Message);
        }
    }

    private string? SafeSaveFolder()
    {
        try { return KnownFolders.ResolveSaveFolder(_settings.Current); }
        catch { return null; }
    }

    /// <summary>
    /// Tray apps have no foreground window, so a bare dialog can open behind other apps.
    /// An invisible, foregrounded owner window fixes that.
    /// </summary>
    private static Window CreateDialogHost()
    {
        var mon = Monitors.FromCursor();
        var host = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Width = 1,
            Height = 1,
            Topmost = true,
        };
        host.SourceInitialized += (_, _) =>
        {
            WindowHelper.MakeToolWindow(host);
            var wa = mon.WorkArea;
            WindowHelper.SetPhysicalBounds(host, new PxRect(wa.X + wa.Width / 2, wa.Y + wa.Height / 3, 1, 1));
        };
        host.Show();
        NativeMethods.ForceForeground(new WindowInteropHelper(host).Handle);
        return host;
    }
}
