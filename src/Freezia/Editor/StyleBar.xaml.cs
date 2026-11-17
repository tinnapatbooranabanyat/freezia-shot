using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Freezia.Core;

namespace Freezia.Editor;

/// <summary>
/// Style row for an <see cref="EditSurface"/>: stroke / fill colors (with the full <see cref="ColorPicker"/>
/// popup and its eyedropper), presets, saved and recent colors, and the size slider.
/// Saved / recent colors are stored in <c>settings.Current.Editor</c>, shared by every editing surface.
/// </summary>
public partial class StyleBar : UserControl
{
    private enum ColorTarget { Stroke, Fill }

    public static readonly Color[] Presets =
    {
        Color.FromRgb(0xFF, 0x3B, 0x30), // red
        Color.FromRgb(0xFF, 0x95, 0x00), // orange
        Color.FromRgb(0xFF, 0xCC, 0x00), // yellow
        Color.FromRgb(0x34, 0xC7, 0x59), // green
        Color.FromRgb(0x00, 0x7A, 0xFF), // blue
        Color.FromRgb(0xAF, 0x52, 0xDE), // purple
        Color.FromRgb(0x00, 0x00, 0x00), // black
        Color.FromRgb(0xFF, 0xFF, 0xFF), // white
    };

    private EditSurface? _surface;
    private SettingsService? _settings;
    private ColorTarget _target = ColorTarget.Stroke;
    private bool _updatingUi;

    // color picker session
    private ColorTarget _pickerTarget;
    private Color _pickerOriginalColor;
    private bool _pickerOriginalFillOn;
    private DateTime _pickerClosedAt;
    private Window? _hostWindow;

    public StyleBar()
    {
        InitializeComponent();
        foreach (var c in Presets) PresetPanel.Children.Add(MakeSwatch(c, removable: false));
        SizeSlider.ValueChanged += OnSizeSliderChanged;

        Picker.ColorChanged += c => ApplyPickerColor(c);
        Picker.Committed += OnPickerCommitted;
        Picker.Cancelled += OnPickerCancelled;
        Picker.EyedropperToggled += on => { if (_surface != null) _surface.CursorOverride = on ? Cursors.Cross : null; };
        Picker.SaveRequested += SaveColor;

        Loaded += (_, _) =>
        {
            Loc.Changed -= ApplyLanguage;
            Loc.Changed += ApplyLanguage;
            HookWindow(Window.GetWindow(this));
        };
        Unloaded += (_, _) =>
        {
            Loc.Changed -= ApplyLanguage;
            HookWindow(null);
        };
    }

    public bool IsPickerOpen => PickerPopup.IsOpen;
    public bool IsEyedropperActive => PickerPopup.IsOpen && Picker.IsEyedropperActive;
    public bool PickerHasKeyboardFocus => PickerPopup.IsOpen && Picker.IsKeyboardFocusWithin;

    public void Attach(EditSurface surface, SettingsService settings)
    {
        if (_surface != null)
        {
            _surface.StateChanged -= UpdateStyleUi;
            _surface.PreviewPointerDown = null;
        }
        _surface = surface;
        _settings = settings;
        surface.StateChanged += UpdateStyleUi;
        surface.PreviewPointerDown = OnSurfacePointerDown;
        BuildSaved();
        BuildRecent();
        ApplyLanguage();
    }

    /// <summary>Close the picker the way a click elsewhere does (keeps the color).</summary>
    public void CommitPicker()
    {
        if (PickerPopup.IsOpen) Picker.Commit();
    }

    public void CancelPicker()
    {
        if (PickerPopup.IsOpen) Picker.Cancel();
    }

    private void HookWindow(Window? w)
    {
        if (_hostWindow == w) return;
        if (_hostWindow != null)
        {
            _hostWindow.PreviewMouseDown -= OnWindowPreviewMouseDown;
            _hostWindow.Deactivated -= OnWindowDeactivated;
            _hostWindow.LocationChanged -= OnWindowMoved;
            _hostWindow.SizeChanged -= OnWindowMoved;
        }
        _hostWindow = w;
        if (w != null)
        {
            w.PreviewMouseDown += OnWindowPreviewMouseDown;
            w.Deactivated += OnWindowDeactivated;
            w.LocationChanged += OnWindowMoved;
            w.SizeChanged += OnWindowMoved;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (PickerPopup.IsOpen && !Picker.IsEyedropperActive) Picker.Commit();
    }

    private void OnWindowMoved(object? sender, EventArgs e) => RepositionPopup();

    /// <summary>Clicks anywhere else in the host window close the picker, keeping the color.</summary>
    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!PickerPopup.IsOpen) return;
        // Mouse events inside the Popup bubble/tunnel through its logical parent (this window),
        // so ignore everything that happens inside the picker itself.
        if (Picker.IsMouseOver || IsInside(e.OriginalSource as DependencyObject, Picker)) return;
        if (!Picker.IsEyedropperActive)
        {
            Picker.Commit();
            if (_surface?.InputElement?.IsMouseOver == true) e.Handled = true; // the closing click does not draw
        }
    }

    /// <summary>Pointer press on the surface while the picker is open: eyedropper pick, or close without drawing.</summary>
    private bool OnSurfacePointerDown(Point p)
    {
        if (!PickerPopup.IsOpen || _surface == null) return false;
        if (Picker.IsEyedropperActive)
        {
            var img = _surface.Source;
            if (p.X >= 0 && p.Y >= 0 && p.X < img.Width && p.Y < img.Height)
            {
                Picker.SetRgbKeepAlpha(img.GetPixel((int)p.X, (int)p.Y));
                Picker.StopEyedropper();
            }
            return true;
        }
        Picker.Commit();
        return true;
    }

    private static bool IsInside(DependencyObject? d, DependencyObject ancestor)
    {
        while (d != null)
        {
            if (d == ancestor) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    public void ApplyLanguage()
    {
        StrokeTarget.ToolTip = Strings.StrokeColor;
        FillTarget.ToolTip = Strings.FillColor;
        FillToggle.Content = Strings.FillEnabled;
        PresetPanel.ToolTip = Strings.PresetColors;
        RecentPanel.ToolTip = Strings.RecentColors;
        StrokeCaption.Text = Loc.Language == AppLanguage.English ? "Stroke" : "เส้น";
        FillCaption.Text = Loc.Language == AppLanguage.English ? "Fill" : "เติม";
        SavedPanel.ToolTip = Strings.SavedColors;
        if (_settings != null) BuildSaved();
        if (PickerPopup.IsOpen) Picker.SetTitle(_pickerTarget == ColorTarget.Stroke ? Strings.StrokeColor : Strings.FillColor);
        UpdateStyleUi();
    }

    // ================================================================ style row

    private void OnSizeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi || _surface == null) return;
        double v = Math.Round(e.NewValue);
        SizeValue.Text = v.ToString("0");
        if (_surface.SliderIsFont) _surface.ChangeStyle(s => s with { FontSize = v }, mergeable: true);
        else _surface.ChangeStyle(s => s with { Thickness = v }, mergeable: true);
    }

    private void OnFillToggle(object sender, RoutedEventArgs e)
    {
        bool on = FillToggle.IsChecked == true;
        _surface?.ChangeStyle(s => s with { FillOn = on });
    }

    private void UpdateStyleUi()
    {
        if (_surface == null) return;
        var s = _surface;
        _updatingUi = true;
        try
        {
            StrokeSwatch.Fill = ColorUtil.Brush(s.StrokeColor);
            FillSwatch.Fill = ColorUtil.Brush(s.FillColor);
            FillSwatch.Opacity = s.FillOn ? 1 : 0.3;
            FillOffSlash.Visibility = s.FillOn ? Visibility.Collapsed : Visibility.Visible;
            FillToggle.IsChecked = s.FillOn;
            StrokeTarget.IsChecked = _target == ColorTarget.Stroke;
            FillTarget.IsChecked = _target == ColorTarget.Fill;

            if (s.SliderIsFont)
            {
                SizeLabel.Text = Strings.FontSize;
                SizeSlider.Minimum = 8;
                SizeSlider.Maximum = 120;
                SizeSlider.Value = s.FontSizeValue;
                SizeValue.Text = s.FontSizeValue.ToString("0");
            }
            else
            {
                SizeLabel.Text = s.Selected != null ? Strings.Thickness
                    : s.Tool == EditorTool.Eraser ? Strings.EraserSize
                    : s.Tool == EditorTool.Blur ? Strings.BlurStrength
                    : Strings.Thickness;
                SizeSlider.Minimum = 1;
                SizeSlider.Maximum = 30;
                SizeSlider.Value = s.Thickness;
                SizeValue.Text = s.Thickness.ToString("0");
            }
        }
        finally
        {
            _updatingUi = false;
        }
    }

    // ================================================================ swatches

    private Button MakeSwatch(Color c, bool removable)
    {
        var b = new Button
        {
            Style = (Style)FindResource("SwatchButton"),
            Background = ColorUtil.Brush(c),
            ToolTip = c.A == 255 ? ColorUtil.ToHex6(c) : $"{ColorUtil.ToHex6(c)}  {Math.Round(c.A / 2.55)}%",
        };
        b.Click += (_, _) => UseSwatchColor(c);
        if (removable)
        {
            var remove = new MenuItem { Header = Strings.RemoveColor };
            remove.Click += (_, _) => RemoveSavedColor(c);
            b.ContextMenu = new ContextMenu { Items = { remove } };
        }
        return b;
    }

    private void BuildSaved()
    {
        if (_settings == null) return;
        SavedPanel.Children.Clear();
        var add = new Button { Style = (Style)FindResource("AddSwatchButton"), ToolTip = Strings.SaveCurrentColor };
        add.Click += (_, _) =>
        {
            if (_surface != null) SaveColor(_target == ColorTarget.Stroke ? _surface.StrokeColor : _surface.FillColor);
        };
        SavedPanel.Children.Add(add);
        foreach (var hex in _settings.Current.Editor.SavedColors)
            if (ColorUtil.TryParse(hex, out var c)) SavedPanel.Children.Add(MakeSwatch(c, removable: true));
    }

    /// <summary>Keep a color for later sessions (newest first, max <see cref="SettingsService.MaxSavedColors"/>).</summary>
    private void SaveColor(Color c)
    {
        if (_settings == null) return;
        var list = _settings.Current.Editor.SavedColors;
        string hex = ColorUtil.ToHex8(c);
        list.RemoveAll(x => string.Equals(x, hex, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, hex);
        if (list.Count > SettingsService.MaxSavedColors)
            list.RemoveRange(SettingsService.MaxSavedColors, list.Count - SettingsService.MaxSavedColors);
        _settings.Save();
        BuildSaved();
    }

    private void RemoveSavedColor(Color c)
    {
        if (_settings == null) return;
        string hex = ColorUtil.ToHex8(c);
        _settings.Current.Editor.SavedColors.RemoveAll(x => string.Equals(x, hex, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        BuildSaved();
    }

    private void BuildRecent()
    {
        if (_settings == null) return;
        RecentPanel.Children.Clear();
        foreach (var hex in _settings.Current.Editor.RecentColors)
            if (ColorUtil.TryParse(hex, out var c)) RecentPanel.Children.Add(MakeSwatch(c, removable: false));
        var vis = RecentPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentPanel.Visibility = vis;
        RecentSeparator.Visibility = vis;
    }

    private void AddRecent(Color c)
    {
        if (_settings == null) return;
        var list = _settings.Current.Editor.RecentColors;
        string hex = ColorUtil.ToHex8(c);
        list.RemoveAll(x => string.Equals(x, hex, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, hex);
        if (list.Count > 8) list.RemoveRange(8, list.Count - 8);
        _settings.Save();
        BuildRecent();
    }

    private void UseSwatchColor(Color c)
    {
        if (PickerPopup.IsOpen)
        {
            // While the picker is open, swatches feed the picker.
            Picker.SetRgbKeepAlpha(c);
            return;
        }
        if (_target == ColorTarget.Stroke) _surface?.ChangeStyle(s => s with { Stroke = c });
        else _surface?.ChangeStyle(s => s with { Fill = c, FillOn = true });
    }

    // ================================================================ color picker

    private void OnStrokeTargetClick(object sender, RoutedEventArgs e) => OpenPicker(ColorTarget.Stroke);

    private void OnFillTargetClick(object sender, RoutedEventArgs e) => OpenPicker(ColorTarget.Fill);

    private void OpenPicker(ColorTarget target)
    {
        if (_surface == null) return;
        _target = target;
        UpdateStyleUi();
        // The window-level mouse-down just committed a previous picker: a click on the same
        // target button should close it rather than reopen.
        if ((DateTime.Now - _pickerClosedAt).TotalMilliseconds < 300 && _pickerTarget == target) return;

        _surface.BeginColorPreview();
        _pickerTarget = target;
        _pickerOriginalColor = target == ColorTarget.Stroke ? _surface.StrokeColor : _surface.FillColor;
        _pickerOriginalFillOn = _surface.FillOn;

        PickerPopup.PlacementTarget = target == ColorTarget.Stroke ? StrokeTarget : FillTarget;
        Picker.Begin(_pickerOriginalColor, target == ColorTarget.Stroke ? Strings.StrokeColor : Strings.FillColor);
        Picker.FitToScreen((FrameworkElement)PickerPopup.PlacementTarget);
        PickerPopup.IsOpen = true;
        Dispatcher.BeginInvoke(() => Picker.OkButton.Focus(), DispatcherPriority.Input);
    }

    private void RepositionPopup()
    {
        if (!PickerPopup.IsOpen) return;
        double h = PickerPopup.HorizontalOffset;
        PickerPopup.HorizontalOffset = h + 0.01;
        PickerPopup.HorizontalOffset = h;
    }

    /// <summary>Live preview while the picker is open (no undo entries).</summary>
    private void ApplyPickerColor(Color c) => _surface?.PreviewColor(_pickerTarget == ColorTarget.Stroke, c);

    private void OnPickerCommitted(Color c)
    {
        ApplyPickerColor(c);
        _surface?.CommitColorPreview();
        if (c != _pickerOriginalColor) AddRecent(c);
        ClosePicker();
    }

    private void OnPickerCancelled()
    {
        _surface?.CancelColorPreview(_pickerTarget == ColorTarget.Stroke, _pickerOriginalColor, _pickerOriginalFillOn);
        ClosePicker();
    }

    private void ClosePicker()
    {
        PickerPopup.IsOpen = false;
        _pickerClosedAt = DateTime.Now;
        if (_surface != null)
        {
            _surface.CursorOverride = null;
            _surface.RefreshCursor();
        }
        Window.GetWindow(this)?.Focus();
    }
}
