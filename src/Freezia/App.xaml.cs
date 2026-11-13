using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Freezia.Capture;
using Freezia.Core;
using Freezia.UI;

namespace Freezia;

public partial class App : Application
{
    private SingleInstance? _instance;
    private TrayIcon? _tray;
    private HotkeyManager? _hotkeys;
    private CaptureController? _capture;
    private readonly UpdateService _updates = new();
    private DispatcherTimer? _updateTimer;
    private string? _notifiedVersion;
    private UpdateWindow? _updateWindow;

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

        Logger.Info($"Freezia {AppInfo.Version} starting");
        Settings.Load();
        if (Settings.IsFirstRun) ApplyInstallerLanguage();
        Loc.Set(Settings.Current.Language);
        ThemeManager.Apply(Settings.Current.ThemeId, ParseAccent(Settings.Current.CustomBackground));
        ThemeManager.SetAccent(ParseAccent(Settings.Current.AccentColor));
        ScreenCapture.PreferWgc = Settings.Current.CaptureMethod == CaptureMethod.Auto;
        if (ScreenCapture.PreferWgc && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
            System.Threading.Tasks.Task.Run(WgcCapture.Warmup);

        _instance.Listen(
            // launching Freezia again while it runs opens the settings window
            onActivate: () => Dispatcher.BeginInvoke(OpenSettings),
            onExit: () => Dispatcher.BeginInvoke(ExitApp));

        _tray = new TrayIcon(Settings.Current);
        _tray.UpdateHotkeys(Settings.Current);
        _tray.CaptureRegion += () => CaptureRegion();
        _tray.CaptureScreen += () => CaptureScreen();
        _tray.CaptureAll += () => CaptureAllScreens();
        _tray.OpenFolder += OpenSaveFolder;
        _tray.OpenSettings += OpenSettings;
        _tray.ExitRequested += ExitApp;
        _tray.OpenHistory += OpenHistory;
        _tray.PickColor += () => _capture?.PickColor();
        _tray.OpenUpdate += () => { if (_updates.Available is UpdateInfo u) OpenUpdate(u); };

        _hotkeys = new HotkeyManager(Dispatcher);
        _hotkeys.SetBindings(Settings.Current);
        _hotkeys.Triggered += OnHotkey;
        if (!_hotkeys.Start())
            ToastWindow.ShowToast(Strings.HotkeyHookFailed, error: true);

        _capture = new CaptureController(Settings, _hotkeys);
        _capture.OpenEditor = OpenEditor;

        StartUpdateChecks();

        Settings.Changed += (_, _) =>
        {
            _tray?.UpdateHotkeys(Settings.Current);
            _hotkeys?.SetBindings(Settings.Current);
            Loc.Set(Settings.Current.Language);
            ThemeManager.Apply(Settings.Current.ThemeId, ParseAccent(Settings.Current.CustomBackground));
            ThemeManager.SetAccent(ParseAccent(Settings.Current.AccentColor));
            ScreenCapture.PreferWgc = Settings.Current.CaptureMethod == CaptureMethod.Auto;
        };
    }

    // ---- Updates ----

    private void StartUpdateChecks()
    {
        UpdateService.CleanDownloads();
        // first check shortly after start-up (not during login rush), then every 6 hours
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            if (Settings.Current.AutoCheckUpdates) await CheckForUpdatesAsync(manual: false);
        };
        _updateTimer.Start();
    }

    /// <summary>Check GitHub for a newer release. Returns the update, or null when up to date (throws on errors if manual).</summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync(bool manual)
    {
        try
        {
            var info = await _updates.CheckAsync();
            if (_tray != null) _tray.Update = info;
            if (info != null)
            {
                Logger.Info($"Update available: {info.Version} ({info.SetupName})");
                if (manual) OpenUpdate(info);
                else if (_notifiedVersion != info.Tag)
                {
                    _notifiedVersion = info.Tag; // once per version per session; the tray item stays
                    ToastWindow.ShowToast(string.Format(Strings.UpdateToastTitle, info.Version.ToString(3)),
                        Strings.UpdateToastMessage, onClick: () => OpenUpdate(info), seconds: 12, glyph: "\uE896");
                }
            }
            return info;
        }
        catch (Exception ex)
        {
            Logger.Error("Update check failed", ex);
            if (manual) throw;
            return null;
        }
    }

    public void OpenUpdate(UpdateInfo info)
    {
        if (_updateWindow != null)
        {
            _updateWindow.Activate();
            return;
        }
        _updateWindow = new UpdateWindow(_updates, info);
        _updateWindow.Closed += (_, _) => _updateWindow = null;
        _updateWindow.InstallerStarted += () =>
        {
            // Setup also asks us to exit; leaving right away keeps the upgrade quick.
            Dispatcher.BeginInvoke(ExitApp, DispatcherPriority.Background);
        };
        _updateWindow.ShowAndActivate();
    }

    public static System.Windows.Media.Color? ParseAccent(string? hex) =>
        Freezia.Editor.ColorUtil.TryParse(hex, out var c) ? c : null;

    /// <summary>
    /// First run: use the language chosen in the installer (written to HKCU\Software\Freezia by Setup).
    /// </summary>
    private static void ApplyInstallerLanguage()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Freezia");
            if (key?.GetValue("InstallerLanguage") is string lang)
            {
                Settings.Current.Language = lang.Equals("english", StringComparison.OrdinalIgnoreCase)
                    ? AppLanguage.English
                    : AppLanguage.Thai;
                Settings.Save();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Reading installer language failed", ex);
        }
    }

    public HotkeyManager? Hotkeys => _hotkeys;
    public CaptureController? Capture => _capture;

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.CaptureRegion: CaptureRegion(); break;
            case HotkeyAction.CaptureScreen: CaptureScreen(); break;
            case HotkeyAction.CaptureAllScreens: CaptureAllScreens(); break;
            case HotkeyAction.PickColor: _capture?.PickColor(); break;
        }
    }

    // ---- Commands (filled in by later phases) ----

    public void CaptureRegion() => _capture?.CaptureRegion();

    public void CaptureScreen() => _capture?.CaptureScreen();

    public void CaptureAllScreens() => _capture?.CaptureAllScreens();

    private void OpenEditor(PixelImage image)
    {
        try
        {
            var editor = new Freezia.Editor.EditorWindow(image, _capture!, Settings);
            editor.Show();
            Freezia.Native.NativeMethods.ForceForeground(new System.Windows.Interop.WindowInteropHelper(editor).Handle);
            editor.Activate();
        }
        catch (Exception ex)
        {
            Logger.Error("Editor failed to open", ex);
            ToastWindow.ShowToast(Strings.ErrorTitle, ex.Message, error: true);
        }
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

    private HistoryWindow? _historyWindow;

    public void OpenHistory()
    {
        if (_capture == null) return;
        if (_historyWindow != null)
        {
            if (_historyWindow.WindowState == WindowState.Minimized) _historyWindow.WindowState = WindowState.Normal;
            _historyWindow.Activate();
            return;
        }
        _historyWindow = new HistoryWindow(_capture);
        _historyWindow.Closed += (_, _) => _historyWindow = null;
        _historyWindow.Show();
        Freezia.Native.NativeMethods.ForceForeground(new System.Windows.Interop.WindowInteropHelper(_historyWindow).Handle);
        _historyWindow.Activate();
    }

    private SettingsWindow? _settingsWindow;

    public void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(Settings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        Freezia.Native.NativeMethods.ForceForeground(new System.Windows.Interop.WindowInteropHelper(_settingsWindow).Handle);
        _settingsWindow.Activate();
    }

    public void ExitApp()
    {
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateTimer?.Stop();
        _updates.Dispose();
        _hotkeys?.Dispose();
        _hotkeys = null;
        _tray?.Dispose();
        _tray = null;
        _instance?.Dispose();
        _instance = null;
        ThemeManager.Shutdown();
        Logger.Info("Freezia exited");
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
