using System;
using System.IO;
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

        var session = new CaptureSession(shot, Monitors.GetAll());
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
        _hotkeys.SetCaptureHandler(null);
        _session = null;
        if (image == null) return;

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
        if (ClipboardHelper.SetImage(image))
        {
            ToastWindow.ShowToast(Strings.ToastCopied, $"{image.PixelWidth} × {image.PixelHeight} px",
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
