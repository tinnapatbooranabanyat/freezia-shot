using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Freezia.Core;
using Freezia.Native;

namespace Freezia.UI;

/// <summary>"A new version is available": release notes, Update now / Later, download progress.</summary>
public sealed class UpdateWindow : Window
{
    private readonly UpdateService _service;
    private readonly UpdateInfo _info;
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _subtitle = new() { FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _notesLabel = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6) };
    private readonly TextBox _notes = new()
    {
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top,
        FontSize = 13,
        Padding = new Thickness(10, 8, 10, 8),
        MinHeight = 120,
    };
    private readonly ProgressBar _progress = new() { Height = 6, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _update = new() { MinWidth = 120, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
    private readonly Button _later = new() { MinWidth = 96 };
    private CancellationTokenSource? _cts;

    /// <summary>Raised just before the installer is started (the app should exit).</summary>
    public event Action? InstallerStarted;

    public UpdateWindow(UpdateService service, UpdateInfo info)
    {
        _service = service;
        _info = info;
        Width = 520;
        Height = 460;
        MinWidth = 400;
        MinHeight = 320;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(FontFamilyProperty, "UiFont");
        Icon = new BitmapImage(new Uri("pack://application:,,,/Freezia;component/Assets/app.ico"));
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        _notesLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        _progress.SetResourceReference(ForegroundProperty, "AccentBrush");
        _progress.SetResourceReference(BackgroundProperty, "BorderBrush");
        _progress.BorderThickness = new Thickness(0);
        _update.Style = (Style)FindResource("AccentButton");
        _update.Click += (_, _) => OnUpdate();
        _later.Click += (_, _) => { _cts?.Cancel(); Close(); };
        _notes.Text = UpdateService.PlainNotes(info.Notes);

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var logo = new Image { Source = Icon, Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Top };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        header.Children.Add(logo);
        var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(_title);
        titles.Children.Add(_subtitle);
        header.Children.Add(titles);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(_later);
        buttons.Children.Add(_update);

        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_notesLabel, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(_progress, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(_notesLabel);
        root.Children.Add(buttons);
        root.Children.Add(_status);
        root.Children.Add(_progress);
        root.Children.Add(_notes);
        Content = root;

        ApplyLanguage();
        Loc.Changed += ApplyLanguage;
        Closed += (_, _) => { Loc.Changed -= ApplyLanguage; _cts?.Cancel(); };
        WindowHelper.ApplyChrome(this);
        WindowHelper.CenterOnCursorMonitor(this);
    }

    private void ApplyLanguage()
    {
        Title = Strings.UpdateWindowTitle;
        _title.Text = string.Format(Strings.UpdateAvailableTitle, _info.Version.ToString(3));
        _subtitle.Text = string.Format(Strings.UpdateCurrentVersion, AppInfo.Version);
        _notesLabel.Text = Strings.UpdateWhatsNew;
        if (_cts == null)
        {
            _update.Content = UpdateService.IsInstalled ? Strings.UpdateNow : Strings.UpdateOpenPage;
            _later.Content = Strings.UpdateLater;
        }
        else _later.Content = Strings.SettingsCancel;
    }

    public void ShowAndActivate()
    {
        Show();
        NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
    }

    private async void OnUpdate()
    {
        if (!UpdateService.IsInstalled)
        {
            // portable / development copy: there is no installation to upgrade in place
            UpdateService.OpenReleasePage(_info.PageUrl);
            Close();
            return;
        }

        _cts = new CancellationTokenSource();
        _update.IsEnabled = false;
        _later.Content = Strings.SettingsCancel;
        _progress.Visibility = _status.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = false;
        _status.Text = Strings.UpdateDownloading;
        var progress = new Progress<double>(p =>
        {
            _progress.Value = p;
            _status.Text = string.Format(Strings.UpdateDownloadingPercent, (int)(p * 100),
                _info.SetupSize > 0 ? $"{_info.SetupSize / 1048576.0:0.#} MB" : "");
        });
        try
        {
            string setup = await _service.DownloadAsync(_info, progress, _cts.Token);
            _status.Text = Strings.UpdateInstalling;
            _progress.IsIndeterminate = true;
            Logger.Info($"Starting update installer {setup}");
            UpdateService.LaunchInstaller(setup);
            InstallerStarted?.Invoke();
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // window closed / Cancel
        }
        catch (Exception ex)
        {
            Logger.Error("Update failed", ex);
            _progress.Visibility = Visibility.Collapsed;
            _status.Text = string.Format(Strings.UpdateFailed, ex is UpdateVerifyException ? ex.Message : ex.Message);
            _status.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            _cts = null;
            _update.IsEnabled = true;
            _update.Content = Strings.UpdateRetry;
            _later.Content = Strings.Close;
        }
    }
}
