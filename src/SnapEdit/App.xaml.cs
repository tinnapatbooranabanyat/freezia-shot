using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SnapEdit.Core;
using SnapEdit.UI;

namespace SnapEdit;

public partial class App : Application
{
    private SingleInstance? _instance;
    private TrayIcon? _tray;

    public static SettingsService Settings { get; } = new();

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterCrashHandlers();

        bool exitRequest = e.Args.Any(a => string.Equals(a, "--exit", StringComparison.OrdinalIgnoreCase));

        _instance = new SingleInstance();
        if (!_instance.IsFirstInstance)
        {
            if (exitRequest)
            {
                // Used by the installer/uninstaller: ask the running copy to quit and wait for it.
                SingleInstance.Signal(AppInfo.ExitEventName);
                _instance.WaitForOtherToExit(TimeSpan.FromSeconds(10));
            }
            else
            {
                SingleInstance.Signal(AppInfo.ActivateEventName);
            }
            _instance.Dispose();
            _instance = null;
            Shutdown();
            return;
        }
        if (exitRequest)
        {
            // Nothing running: nothing to do.
            Shutdown();
            return;
        }

        Logger.Info($"SnapEdit {AppInfo.Version} starting");
        Settings.Load();

        _instance.Listen(
            onActivate: () => Dispatcher.BeginInvoke(() => ToastWindow.ShowToast(Strings.AlreadyRunning)),
            onExit: () => Dispatcher.BeginInvoke(ExitApp));

        _tray = new TrayIcon();
        _tray.UpdateHotkeys(Settings.Current);
        _tray.CaptureRegion += () => CaptureRegion();
        _tray.CaptureScreen += () => CaptureScreen();
        _tray.CaptureAll += () => CaptureAllScreens();
        _tray.OpenFolder += OpenSaveFolder;
        _tray.OpenSettings += OpenSettings;
        _tray.ExitRequested += ExitApp;

        Settings.Changed += (_, _) => _tray?.UpdateHotkeys(Settings.Current);
    }

    // ---- Commands (filled in by later phases) ----

    public void CaptureRegion()
    {
        Logger.Info("CaptureRegion requested");
    }

    public void CaptureScreen()
    {
        Logger.Info("CaptureScreen requested");
    }

    public void CaptureAllScreens()
    {
        Logger.Info("CaptureAll requested");
    }

    public void OpenSaveFolder()
    {
        try
        {
            string folder = KnownFolders.ResolveSaveFolder(Settings.Current);
            ToastWindow.RevealInExplorer(folder);
        }
        catch (Exception ex)
        {
            Logger.Error("Open folder failed", ex);
        }
    }

    public void OpenSettings()
    {
        Logger.Info("OpenSettings requested");
    }

    public void ExitApp()
    {
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _tray = null;
        _instance?.Dispose();
        _instance = null;
        Logger.Info("SnapEdit exited");
        base.OnExit(e);
    }

    // ---- Crash handling ----

    private void RegisterCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ReportCrash(args.Exception);
            args.Handled = true; // keep the tray app alive
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Logger.Error("Unhandled (AppDomain)", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    private static bool _showingCrash;

    private static void ReportCrash(Exception ex)
    {
        Logger.Error("Unhandled (Dispatcher)", ex);
        if (_showingCrash) return;
        _showingCrash = true;
        try
        {
            MessageBox.Show(string.Format(Strings.UnexpectedError, ex.Message, Logger.LogDir),
                Strings.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _showingCrash = false;
        }
    }
}
