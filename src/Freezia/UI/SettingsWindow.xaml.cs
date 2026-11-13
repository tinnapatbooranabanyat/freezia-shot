using System;
using System.Windows;
using Freezia.Core;

namespace Freezia.UI;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _service;
    private readonly AppSettings _edit;
    private string? _folder;

    public SettingsWindow(SettingsService service)
    {
        InitializeComponent();
        _service = service;
        _edit = SettingsService.Clone(service.Current);

        _originalLanguage = Loc.Language;
        _originalTheme = ThemeManager.Mode;
        LangTh.IsChecked = _edit.Language == AppLanguage.Thai;
        LangEn.IsChecked = _edit.Language == AppLanguage.English;
        ThemeDark.IsChecked = _edit.Theme == AppTheme.Dark;
        ThemeLight.IsChecked = _edit.Theme == AppTheme.Light;
        ThemeSystem.IsChecked = _edit.Theme == AppTheme.System;
        _ready = true;

        ApplyLanguage();
        Loc.Changed += ApplyLanguage;
        Closed += (_, _) => Loc.Changed -= ApplyLanguage;

        // hotkeys
        BoxRegion.Hotkey = _edit.HotkeyRegion;
        BoxScreen.Hotkey = _edit.HotkeyScreen;
        BoxAll.Hotkey = _edit.HotkeyAllScreens;
        foreach (var box in new[] { BoxRegion, BoxScreen, BoxAll })
        {
            box.Validate = ValidateHotkey;
            box.Message += (_, msg) => ShowHotkeyError(msg);
            box.RecordingStarted += b =>
            {
                foreach (var other in new[] { BoxRegion, BoxScreen, BoxAll })
                    if (other != b) other.StopRecording();
                ShowHotkeyError(null);
            };
        }

        // saving
        _folder = string.IsNullOrWhiteSpace(_edit.SaveFolder) ? null : _edit.SaveFolder;
        UpdateFolderUi();
        FmtPng.IsChecked = _edit.Format == ImageFormatKind.Png;
        FmtJpg.IsChecked = _edit.Format == ImageFormatKind.Jpg;
        QualitySlider.Value = _edit.JpegQuality;
        QualityValue.Text = _edit.JpegQuality.ToString();
        QualitySlider.ValueChanged += (_, e) => QualityValue.Text = ((int)e.NewValue).ToString();
        UpdateJpegPanel();

        AfterEditor.IsChecked = _edit.AfterFullCapture == AfterCaptureAction.OpenEditor;
        AfterCopy.IsChecked = _edit.AfterFullCapture == AfterCaptureAction.Copy;
        AfterSave.IsChecked = _edit.AfterFullCapture == AfterCaptureAction.Save;

        // the registry is the source of truth (the installer may have set it)
        StartupToggle.IsChecked = StartupManager.IsEnabled;

        WindowHelper.ApplyChrome(this);
        WindowHelper.CenterOnCursorMonitor(this);
        Deactivated += (_, _) => StopAllRecording();
        Closed += (_, _) =>
        {
            StopAllRecording();
            if (!_saved)
            {
                Loc.Set(_originalLanguage);
                ThemeManager.Apply(_originalTheme);
            }
        };
    }

    private readonly AppLanguage _originalLanguage;
    private readonly AppTheme _originalTheme;
    private readonly bool _ready;
    private bool _saved;

    private void ApplyLanguage()
    {
        Title = Strings.SettingsTitle;
        HeaderTitle.Text = Strings.TraySettings;
        AboutText.Text = string.Format(Strings.SettingsAbout, AppInfo.Version);
        HotkeysHeader.Text = Strings.SettingsHotkeys;
        LblRegion.Text = Strings.SettingsHotkeyRegion;
        LblScreen.Text = Strings.SettingsHotkeyScreen;
        LblAll.Text = Strings.SettingsHotkeyAll;
        HotkeyHelp.Text = Strings.SettingsHotkeyHelp;
        SnippingNote.Text = Strings.SettingsSnippingNote;
        SaveHeader.Text = Strings.SettingsSave;
        LblFolder.Text = Strings.SettingsFolder;
        BrowseButton.Content = Strings.SettingsBrowse;
        ResetFolderButton.Content = Strings.SettingsResetFolder;
        LblFormat.Text = Strings.SettingsFormat;
        LblQuality.Text = Strings.SettingsJpegQuality;
        AfterHeader.Text = Strings.SettingsAfterCapture;
        AfterEditor.Content = Strings.SettingsAfterEditor;
        AfterCopy.Content = Strings.SettingsAfterCopy;
        AfterSave.Content = Strings.SettingsAfterSave;
        GeneralHeader.Text = Strings.SettingsGeneral;
        StartupToggle.Content = Strings.SettingsStartup;
        OkButton.Content = Strings.SettingsOk;
        CancelButton.Content = Strings.SettingsCancel;

        AppearanceHeader.Text = Strings.SettingsAppearance;
        LblLanguage.Text = Strings.SettingsLanguage;
        LblTheme.Text = Strings.SettingsTheme;
        ThemeDark.Content = Strings.ThemeDark;
        ThemeLight.Content = Strings.ThemeLight;
        ThemeSystem.Content = Strings.ThemeSystem;
        BoxRegion.Label = Strings.SettingsHotkeyRegion;
        BoxScreen.Label = Strings.SettingsHotkeyScreen;
        BoxAll.Label = Strings.SettingsHotkeyAll;
        if (FolderHint != null) UpdateFolderUi();
    }

    // Language and theme are previewed live; Cancel restores the previous ones.
    private void OnLanguagePicked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        Loc.Set(LangEn.IsChecked == true ? AppLanguage.English : AppLanguage.Thai);
    }

    private void OnThemePicked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ThemeManager.Apply(SelectedTheme);
    }

    private AppTheme SelectedTheme =>
        ThemeLight.IsChecked == true ? AppTheme.Light : ThemeSystem.IsChecked == true ? AppTheme.System : AppTheme.Dark;

    private void StopAllRecording()
    {
        BoxRegion.StopRecording();
        BoxScreen.StopRecording();
        BoxAll.StopRecording();
    }

    // ---------------- hotkeys ----------------

    private string? ValidateHotkey(HotkeyBox box, Hotkey candidate)
    {
        if (candidate.IsEmpty) return null;
        if (candidate.Modifiers == HotkeyModifiers.None && IsTypingKey(candidate.Key))
            return Strings.HotkeyNeedsModifier;
        foreach (var other in new[] { BoxRegion, BoxScreen, BoxAll })
        {
            if (other != box && other.Hotkey == candidate)
                return string.Format(Strings.HotkeyDuplicate, other.Label);
        }
        return null;
    }

    /// <summary>Keys that would break normal typing if used alone as a global hotkey.</summary>
    private static bool IsTypingKey(int vk) =>
        vk is >= 0x30 and <= 0x39      // digits
        or >= 0x41 and <= 0x5A         // letters
        or >= 0x60 and <= 0x6F         // numpad
        or >= 0xBA and <= 0xC0         // ; = , - . / `
        or >= 0xDB and <= 0xDE         // [ \ ] '
        or 0x20 or 0x0D or 0x09 or 0x08 or 0x1B  // space, enter, tab, backspace, esc
        or >= 0x21 and <= 0x28         // page up/down, end, home, arrows
        or 0x2E;                       // delete

    private void ShowHotkeyError(string? message)
    {
        HotkeyError.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        HotkeyErrorText.Text = message ?? "";
    }

    // ---------------- folder / format ----------------

    private void UpdateFolderUi()
    {
        string downloads = KnownFolders.Downloads;
        FolderBox.Text = _folder ?? downloads;
        FolderHint.Text = _folder == null ? Strings.SettingsFolderDefaultHint : "";
        FolderHint.Visibility = _folder == null ? Visibility.Visible : Visibility.Collapsed;
        ResetFolderButton.IsEnabled = _folder != null;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        StopAllRecording();
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.SettingsFolder,
            InitialDirectory = _folder ?? KnownFolders.Downloads,
        };
        if (dlg.ShowDialog(this) == true)
        {
            _folder = dlg.FolderName;
            UpdateFolderUi();
        }
    }

    private void OnResetFolder(object sender, RoutedEventArgs e)
    {
        _folder = null;
        UpdateFolderUi();
    }

    private void OnFormatChanged(object sender, RoutedEventArgs e) => UpdateJpegPanel();

    private void UpdateJpegPanel()
    {
        if (JpegPanel != null) JpegPanel.Visibility = FmtJpg.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- save / cancel ----------------

    private void OnOk(object sender, RoutedEventArgs e)
    {
        StopAllRecording();

        // final duplicate check (defensive: the boxes already validate while recording)
        var boxes = new[] { BoxRegion, BoxScreen, BoxAll };
        for (int i = 0; i < boxes.Length; i++)
            for (int j = i + 1; j < boxes.Length; j++)
                if (!boxes[i].Hotkey.IsEmpty && boxes[i].Hotkey == boxes[j].Hotkey)
                {
                    ShowHotkeyError(string.Format(Strings.HotkeyDuplicate, boxes[i].Label));
                    return;
                }

        _edit.HotkeyRegion = BoxRegion.Hotkey;
        _edit.HotkeyScreen = BoxScreen.Hotkey;
        _edit.HotkeyAllScreens = BoxAll.Hotkey;
        _edit.SaveFolder = _folder;
        _edit.Format = FmtJpg.IsChecked == true ? ImageFormatKind.Jpg : ImageFormatKind.Png;
        _edit.JpegQuality = (int)QualitySlider.Value;
        _edit.AfterFullCapture = AfterCopy.IsChecked == true ? AfterCaptureAction.Copy
            : AfterSave.IsChecked == true ? AfterCaptureAction.Save
            : AfterCaptureAction.OpenEditor;

        _edit.Language = LangEn.IsChecked == true ? AppLanguage.English : AppLanguage.Thai;
        _edit.Theme = SelectedTheme;

        bool startup = StartupToggle.IsChecked == true;
        _edit.StartWithWindows = startup;
        // keep editor colors/recents that may have changed while this window was open
        _edit.Editor = _service.Current.Editor;

        try
        {
            if (startup != StartupManager.IsEnabled) StartupManager.SetEnabled(startup);
        }
        catch (Exception ex)
        {
            ToastWindow.ShowToast(Strings.SettingsStartupFailed, ex.Message, error: true);
        }

        _saved = true;
        _service.Update(_edit);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
