using System;
using System.Drawing;
using System.Windows.Forms;
using Freezia.Core;

namespace Freezia.UI;

/// <summary>
/// System tray icon (WinForms NotifyIcon). The right-click menu is a WPF window (<see cref="TrayMenu"/>)
/// so it follows the app theme, language and font.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _appIcon;
    private AppSettings _settings;
    private TrayMenu? _menu;

    public event Action? CaptureRegion;
    public event Action? CaptureScreen;
    public event Action? CaptureAll;
    public event Action? OpenFolder;
    public event Action? OpenSettings;
    public event Action? ExitRequested;

    public TrayIcon(AppSettings settings)
    {
        _settings = settings;
        _appIcon = LoadIcon();
        _icon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = Strings.TrayTooltip,
            Visible = true,
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) ShowMenu();
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Delayed(() => CaptureRegion?.Invoke(), 150);
        };
        Loc.Changed += OnLanguageChanged;
    }

    public void UpdateHotkeys(AppSettings s) => _settings = s;

    private void OnLanguageChanged() => _icon.Text = Strings.TrayTooltip;

    private static string Key(Hotkey h) => h == null || h.IsEmpty ? "" : h.ToString();

    private void ShowMenu()
    {
        _menu?.Close();
        var m = new TrayMenu();
        // Capture items are delayed so the menu's fade-out is gone before the screen is frozen.
        m.AddItem(TrayMenu.IconRegion, Strings.TrayCaptureRegion, Key(_settings.HotkeyRegion), () => Delayed(() => CaptureRegion?.Invoke(), 200), bold: true);
        m.AddItem(TrayMenu.IconScreen, Strings.TrayCaptureScreen, Key(_settings.HotkeyScreen), () => Delayed(() => CaptureScreen?.Invoke(), 200));
        m.AddItem(TrayMenu.IconAll, Strings.TrayCaptureAll, Key(_settings.HotkeyAllScreens), () => Delayed(() => CaptureAll?.Invoke(), 200));
        m.AddSeparator();
        m.AddGlyphItem("", Strings.TrayOpenFolder, () => OpenFolder?.Invoke());
        m.AddGlyphItem("", Strings.TraySettings, () => OpenSettings?.Invoke());
        m.AddSeparator();
        m.AddGlyphItem("", Strings.TrayExit, () => ExitRequested?.Invoke());
        m.Closed += (_, _) => { if (_menu == m) _menu = null; };
        _menu = m;
        m.ShowAtCursor();
    }

    private static void Delayed(Action action, int ms)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Freezia;component/Assets/app.ico");
            var info = System.Windows.Application.GetResourceStream(uri);
            if (info != null)
            {
                using var stream = info.Stream;
                return new Icon(stream, SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Tray icon load failed", ex);
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        Loc.Changed -= OnLanguageChanged;
        _menu?.Close();
        _icon.Visible = false;
        _icon.Dispose();
        _appIcon.Dispose();
    }
}
