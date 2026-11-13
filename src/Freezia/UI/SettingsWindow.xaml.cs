using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freezia.Editor;
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
        _originalThemeId = ThemeManager.ThemeId;
        _originalCustomBg = ThemeManager.CustomBackground;
        _themeId = ThemeManager.ThemeId;
        _customBg = ThemeManager.CustomBackground;
        LangTh.IsChecked = _edit.Language == AppLanguage.Thai;
        LangEn.IsChecked = _edit.Language == AppLanguage.English;
        BuildThemeCards();
        _originalAccent = ThemeManager.Accent;
        _accent = ThemeManager.Accent;
        BuildAccentSwatches();
        AccentPicker.ShowAlpha = false;
        AccentPicker.ShowImageTools = false;
        // one color picker popup, shared by "custom accent" and "custom background"
        AccentPicker.ColorChanged += c => { if (_pickingBackground) PreviewCustomBackground(c); else PreviewAccent(c); };
        AccentPicker.Committed += c =>
        {
            if (_pickingBackground) PreviewCustomBackground(c); else PreviewAccent(c);
            AccentPopup.IsOpen = false;
        };
        AccentPicker.Cancelled += () =>
        {
            if (_pickingBackground)
            {
                _customBg = _bgBeforePicker;
                _themeId = _themeBeforePicker;
                ThemeManager.Apply(_themeId, _customBg);
                BuildThemeCards();
            }
            else PreviewAccent(_accentBeforePicker);
            AccentPopup.IsOpen = false;
        };
        _ready = true;

        ApplyLanguage();
        Loc.Changed += ApplyLanguage;
        Closed += (_, _) => Loc.Changed -= ApplyLanguage;

        // hotkeys
        BoxRegion.Hotkey = _edit.HotkeyRegion;
        BoxScreen.Hotkey = _edit.HotkeyScreen;
        BoxAll.Hotkey = _edit.HotkeyAllScreens;
        BoxColor.Hotkey = _edit.HotkeyColorPicker;
        HistoryToggle.IsChecked = _edit.HistoryEnabled;
        HistoryLimitSlider.Value = Math.Clamp(_edit.HistoryLimit, 10, 200);
        HistoryLimitValue.Text = ((int)HistoryLimitSlider.Value).ToString();
        HistoryLimitSlider.ValueChanged += (_, e) => HistoryLimitValue.Text = ((int)e.NewValue).ToString();
        HistoryToggle.Click += (_, _) => HistoryPanel.IsEnabled = HistoryToggle.IsChecked == true;
        HistoryPanel.IsEnabled = _edit.HistoryEnabled;
        foreach (var box in new[] { BoxRegion, BoxScreen, BoxAll, BoxColor })
        {
            box.Validate = ValidateHotkey;
            box.Message += (_, msg) => ShowHotkeyError(msg);
            box.RecordingStarted += b =>
            {
                foreach (var other in new[] { BoxRegion, BoxScreen, BoxAll, BoxColor })
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
                ThemeManager.Apply(_originalThemeId, _originalCustomBg);
                ThemeManager.SetAccent(_originalAccent);
            }
        };
    }

    private readonly AppLanguage _originalLanguage;
    private readonly string _originalThemeId;
    private readonly Color? _originalCustomBg;
    private string _themeId;
    private Color? _customBg;
    private bool _pickingBackground;
    private Color? _bgBeforePicker;
    private string _themeBeforePicker = "dark";
    private RadioButton? _customThemeCard;
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
        LblColor.Text = Strings.SettingsHotkeyColor;
        HistoryToggle.Content = Strings.SettingsHistory;
        LblHistoryLimit.Text = Strings.SettingsHistoryLimit;
        HistoryHint.Text = Strings.SettingsHistoryHint;
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
        LblAccent.Text = Strings.SettingsAccent;
        AccentHint.Text = Strings.SettingsAccentHint;
        if (AccentPanel.Children.Count > 0) BuildAccentSwatches();
        ThemeHint.Text = Strings.SettingsThemeHint;
        if (ThemePanel.Children.Count > 0) BuildThemeCards();
        BoxRegion.Label = Strings.SettingsHotkeyRegion;
        BoxScreen.Label = Strings.SettingsHotkeyScreen;
        BoxAll.Label = Strings.SettingsHotkeyAll;
        BoxColor.Label = Strings.SettingsHotkeyColor;
        if (FolderHint != null) UpdateFolderUi();
    }

    // Language and theme are previewed live; Cancel restores the previous ones.
    private void OnLanguagePicked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        Loc.Set(LangEn.IsChecked == true ? AppLanguage.English : AppLanguage.Thai);
    }

    // ---------------- color themes ----------------

    private void BuildThemeCards()
    {
        ThemePanel.Children.Clear();
        string T(ThemeDef d) => Loc.Language == AppLanguage.English ? d.En : d.Th;

        // follow Windows: half dark, half light
        var sys = new Grid();
        sys.ColumnDefinitions.Add(new ColumnDefinition());
        sys.ColumnDefinitions.Add(new ColumnDefinition());
        var left = Preview(ThemeManager.Themes[0], new CornerRadius(8, 0, 0, 8));
        var right = Preview(ThemeManager.Themes[1], new CornerRadius(0, 8, 8, 0));
        Grid.SetColumn(right, 1);
        sys.Children.Add(left);
        sys.Children.Add(right);
        AddCard(ThemeManager.SystemId, sys, Strings.ThemeSystem);

        foreach (var t in ThemeManager.Themes) AddCard(t.Id, Preview(t, new CornerRadius(8)), T(t));

        // custom background: generated preview, or a "+" until a color was picked
        FrameworkElement customPreview = _customBg is Color cb
            ? Preview(ThemeManager.FromBackground(cb), new CornerRadius(8))
            : new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1.5),
                BorderBrush = (Brush)FindResource("TextDimBrush"),
                Child = new TextBlock
                {
                    Text = "+",
                    FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("TextDimBrush"),
                },
            };
        _customThemeCard = AddCard(ThemeManager.CustomId, customPreview, Strings.ThemeCustom);
        _customThemeCard.ToolTip = Strings.ThemeCustomPick;
    }

    private RadioButton AddCard(string id, FrameworkElement preview, string name)
    {
        preview.Width = 88;
        preview.Height = 56;
        var label = new TextBlock { Text = name, FontSize = 11.5, Margin = new Thickness(0, 5, 0, 1), HorizontalAlignment = HorizontalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var stack = new StackPanel();
        stack.Children.Add(preview);
        stack.Children.Add(label);
        var rb = new RadioButton
        {
            Style = (Style)FindResource("ThemeCard"),
            GroupName = "themes",
            Content = stack,
            IsChecked = id == _themeId,
        };
        rb.Click += (_, _) => PickTheme(id);
        ThemePanel.Children.Add(rb);
        return rb;
    }

    /// <summary>A miniature window in the theme's colors: sidebar, two text lines, an accent button.</summary>
    private static Border Preview(ThemeDef t, CornerRadius corners)
    {
        SolidColorBrush B(Color c) => new(c);
        var canvas = new Canvas { ClipToBounds = true };
        var side = new Border { Width = 22, Height = 56, Background = B(t.Surface) };
        canvas.Children.Add(side);
        void Line(double x, double y, double w, Color c)
        {
            var r = new Border { Width = w, Height = 4, CornerRadius = new CornerRadius(2), Background = B(c) };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            canvas.Children.Add(r);
        }
        Line(5, 9, 12, t.TextDim);
        Line(5, 17, 10, t.TextDim);
        Line(30, 10, 40, t.Text);
        Line(30, 19, 28, t.TextDim);
        var card = new Border { Width = 50, Height = 14, CornerRadius = new CornerRadius(3), Background = B(t.Surface2), BorderBrush = B(t.Border), BorderThickness = new Thickness(1) };
        Canvas.SetLeft(card, 30);
        Canvas.SetTop(card, 28);
        canvas.Children.Add(card);
        var accent = new Border { Width = 18, Height = 7, CornerRadius = new CornerRadius(3.5), Background = B(t.Accent) };
        Canvas.SetLeft(accent, 62);
        Canvas.SetTop(accent, 45);
        canvas.Children.Add(accent);
        return new Border
        {
            CornerRadius = corners,
            Background = B(t.Bg),
            BorderBrush = B(t.Border),
            BorderThickness = new Thickness(1),
            Child = canvas,
            ClipToBounds = true,
        };
    }

    private void PickTheme(string id)
    {
        if (!_ready) return;
        if (id == ThemeManager.CustomId)
        {
            OpenBackgroundPicker();
            return;
        }
        _themeId = id;
        ThemeManager.Apply(_themeId, _customBg);
        RefreshAccentDefaultSwatch();
    }

    private void OpenBackgroundPicker()
    {
        _pickingBackground = true;
        _bgBeforePicker = _customBg;
        _themeBeforePicker = _themeId;
        var start = _customBg ?? ThemeManager.Current.Bg;
        AccentPopup.PlacementTarget = _customThemeCard;
        AccentPicker.Begin(start, Strings.ThemeCustom);
        AccentPopup.IsOpen = true;
        PreviewCustomBackground(start);
    }

    /// <summary>Live preview of a generated theme from one background color.</summary>
    private void PreviewCustomBackground(Color c)
    {
        _customBg = Color.FromRgb(c.R, c.G, c.B);
        _themeId = ThemeManager.CustomId;
        ThemeManager.Apply(_themeId, _customBg);
        if (_customThemeCard != null)
        {
            var stack = (StackPanel)_customThemeCard.Content;
            var p = Preview(ThemeManager.FromBackground(_customBg.Value), new CornerRadius(8));
            p.Width = 88;
            p.Height = 56;
            // a UIElementCollection slot can't be overwritten: remove, then insert
            stack.Children.RemoveAt(0);
            stack.Children.Insert(0, p);
            _customThemeCard.IsChecked = true;
        }
        RefreshAccentDefaultSwatch();
    }

    /// <summary>The "theme default" accent swatch shows the current theme's own accent.</summary>
    private void RefreshAccentDefaultSwatch()
    {
        if (AccentPanel.Children.Count > 0 && AccentPanel.Children[0] is RadioButton first)
            first.Background = new SolidColorBrush(ThemeManager.Current.Accent);
    }

    // ---------------- accent color ----------------

    private readonly Color? _originalAccent;
    private Color? _accent;
    private Color? _accentBeforePicker;
    private RadioButton? _customSwatch;

    private void BuildAccentSwatches()
    {
        AccentPanel.Children.Clear();
        bool matched = false;
        foreach (var (th, en, color) in ThemeManager.AccentPresets)
        {
            // the default blue is shown as the dark palette's blue
            var shown = color ?? ThemeManager.Current.Accent;
            var rb = new RadioButton
            {
                Style = (Style)FindResource("AccentSwatch"),
                GroupName = "accent",
                Background = new SolidColorBrush(shown),
                ToolTip = Loc.Language == AppLanguage.English ? en : th,
                IsChecked = color == _accent,
            };
            matched |= color == _accent;
            var c = color;
            rb.Checked += (_, _) => { if (_ready) PreviewAccent(c); };
            AccentPanel.Children.Add(rb);
        }

        // custom: shows the custom color, or a rainbow when none is set
        Brush customFill = !matched && _accent is Color ac
            ? new SolidColorBrush(ac)
            : new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(0xFF, 0x3B, 0x30), 0),
                new GradientStop(Color.FromRgb(0xFF, 0xCC, 0x00), 0.33),
                new GradientStop(Color.FromRgb(0x34, 0xC7, 0x59), 0.66),
                new GradientStop(Color.FromRgb(0x00, 0x7A, 0xFF), 1),
            }, 45);
        var plus = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,5 H10 M5,0 V10"),
            Stroke = Brushes.White,
            StrokeThickness = 1.8,
            Width = 10,
            Height = 10,
            IsHitTestVisible = false,
        };
        _customSwatch = new RadioButton
        {
            Style = (Style)FindResource("AccentSwatch"),
            GroupName = "accent",
            Background = customFill,
            ToolTip = Strings.AccentCustom,
            IsChecked = !matched,
            Content = matched ? plus : null,
        };
        _customSwatch.Click += (_, _) => OpenAccentPicker();
        AccentPanel.Children.Add(_customSwatch);
    }

    private void OpenAccentPicker()
    {
        _pickingBackground = false;
        _accentBeforePicker = _accent;
        AccentPopup.PlacementTarget = _customSwatch;
        AccentPicker.Begin(_accent ?? ThemeManager.Current.Accent, Strings.SettingsAccent);
        AccentPopup.IsOpen = true;
    }

    /// <summary>Live preview of an accent (null = default blue); saved on OK, restored on Cancel.</summary>
    private void PreviewAccent(Color? c)
    {
        _accent = c;
        ThemeManager.SetAccent(c);
        if (_customSwatch != null && c is Color cc && Array.TrueForAll(ThemeManager.AccentPresets, p => p.Color != cc))
        {
            _customSwatch.Background = new SolidColorBrush(cc);
            _customSwatch.Content = null;
        }
    }


    private void StopAllRecording()
    {
        BoxRegion.StopRecording();
        BoxScreen.StopRecording();
        BoxAll.StopRecording();
        BoxColor.StopRecording();
    }

    // ---------------- hotkeys ----------------

    private string? ValidateHotkey(HotkeyBox box, Hotkey candidate)
    {
        if (candidate.IsEmpty) return null;
        if (candidate.Modifiers == HotkeyModifiers.None && IsTypingKey(candidate.Key))
            return Strings.HotkeyNeedsModifier;
        foreach (var other in new[] { BoxRegion, BoxScreen, BoxAll, BoxColor })
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
        var boxes = new[] { BoxRegion, BoxScreen, BoxAll, BoxColor };
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
        _edit.HotkeyColorPicker = BoxColor.Hotkey;
        _edit.HistoryEnabled = HistoryToggle.IsChecked == true;
        _edit.HistoryLimit = (int)HistoryLimitSlider.Value;
        if (!_edit.HistoryEnabled) HistoryService.Clear();
        else HistoryService.Prune(_edit.HistoryLimit);
        _edit.SaveFolder = _folder;
        _edit.Format = FmtJpg.IsChecked == true ? ImageFormatKind.Jpg : ImageFormatKind.Png;
        _edit.JpegQuality = (int)QualitySlider.Value;
        _edit.AfterFullCapture = AfterCopy.IsChecked == true ? AfterCaptureAction.Copy
            : AfterSave.IsChecked == true ? AfterCaptureAction.Save
            : AfterCaptureAction.OpenEditor;

        _edit.Language = LangEn.IsChecked == true ? AppLanguage.English : AppLanguage.Thai;
        _edit.ThemeId = _themeId;
        _edit.CustomBackground = _customBg is Color bg ? ColorUtil.ToHex6(bg) : null;
        _edit.AccentColor = _accent is Color accent ? ColorUtil.ToHex6(accent) : null;

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
