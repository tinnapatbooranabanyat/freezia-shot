using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Freezia.Core;

namespace Freezia.Editor;

/// <summary>
/// Full RGB(A) color picker: SV square + hue bar + alpha bar + R/G/B sliders and boxes + HEX,
/// eyedropper, old/new preview. Every part updates the others live. The state is kept as
/// HSV + alpha so hue does not jump when saturation/brightness reach 0.
/// </summary>
public partial class ColorPicker : UserControl
{
    private double _h, _s, _v;   // 0..360, 0..1, 0..1
    private byte _a = 255;
    private Color _original;
    private bool _updating;

    /// <summary>Live change (every drag/keystroke).</summary>
    public event Action<Color>? ColorChanged;
    public event Action<Color>? Committed;
    public event Action? Cancelled;
    /// <summary>true = start eyedropper mode on the canvas, false = stop.</summary>
    public event Action<bool>? EyedropperToggled;

    private const double SvFullHeight = 150, SvMinHeight = 70;

    /// <summary>
    /// Keep the whole picker (OK / Cancel included) on the monitor of <paramref name="target"/>: on short
    /// screens the color square gets smaller, and if that is not enough the card scrolls.
    /// Call right after opening the popup.
    /// </summary>
    public void FitToScreen(FrameworkElement target)
    {
        try
        {
            if (PresentationSource.FromVisual(target) is not { CompositionTarget: { } ct }) return;
            double scale = ct.TransformToDevice.M22;
            var center = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
            var mon = Freezia.Capture.Monitors.FromPoint((int)center.X, (int)center.Y);
            FitToHeight(mon.WorkArea.Height / scale - 2 * 12 - 8); // card margin + a little air
        }
        catch (Exception ex)
        {
            Freezia.Core.Logger.Error("Color picker fit failed", ex);
        }
    }

    /// <summary>Make the card at most <paramref name="available"/> DIPs tall (see <see cref="FitToScreen"/>).</summary>
    public void FitToHeight(double available)
    {
        SvArea.Height = SvFullHeight;
        Card.MaxHeight = double.PositiveInfinity;
        Card.Measure(new Size(Card.Width, double.PositiveInfinity));
        double natural = Card.DesiredSize.Height;
        if (natural > available)
            SvArea.Height = Math.Max(SvMinHeight, SvFullHeight - (natural - available));
        Card.MaxHeight = Math.Max(200, available);
    }

    public ColorPicker()
    {
        InitializeComponent();
        ApplyLanguage();
        Loaded += (_, _) => { Loc.Changed -= ApplyLanguage; Loc.Changed += ApplyLanguage; };
        Unloaded += (_, _) => Loc.Changed -= ApplyLanguage;

        RSlider.ValueChanged += (_, _) => OnRgbSlider();
        GSlider.ValueChanged += (_, _) => OnRgbSlider();
        BSlider.ValueChanged += (_, _) => OnRgbSlider();
        foreach (var box in new[] { RBox, GBox, BBox })
        {
            box.TextChanged += (_, _) => OnRgbBox();
            box.PreviewTextInput += DigitsOnly;
            box.GotKeyboardFocus += (s, _) => ((TextBox)s!).SelectAll();
            box.PreviewKeyDown += ArrowStep;
        }
        AlphaBox.PreviewTextInput += DigitsOnly;
        AlphaBox.TextChanged += (_, _) => OnAlphaBox();
        AlphaBox.GotKeyboardFocus += (_, _) => AlphaBox.SelectAll();
        AlphaBox.PreviewKeyDown += ArrowStep;
        HexBox.TextChanged += (_, _) => OnHexBox();
        HexBox.GotKeyboardFocus += (_, _) => HexBox.SelectAll();
        HexBox.LostKeyboardFocus += (_, _) => Refresh();

        SvArea.SizeChanged += (_, _) => Refresh();
        HueArea.SizeChanged += (_, _) => Refresh();
        AlphaArea.SizeChanged += (_, _) => Refresh();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>"Save this color" (+) was clicked.</summary>
    public event Action<Color>? SaveRequested;

    private void ApplyLanguage()
    {
        AlphaLabel.Text = Strings.PickerAlpha + " %";
        OkButton.Content = Strings.PickerOk;
        CancelButton.Content = Strings.PickerCancel;
        EyedropperButton.ToolTip = Strings.PickerEyedropper;
        EyedropperHint.Text = Strings.PickerEyedropperHint;
        SaveColorButton.ToolTip = Strings.SaveCurrentColor;
        OldText.Text = Strings.PickerOld;
        NewText.Text = Strings.PickerNew;
    }

    public void SetTitle(string title) => TitleText.Text = title;

    /// <summary>Show the opacity bar and box (off for opaque-only colors such as the UI accent).</summary>
    public bool ShowAlpha
    {
        set
        {
            var v = value ? Visibility.Visible : Visibility.Collapsed;
            AlphaArea.Visibility = v;
            AlphaLabel.Visibility = v;
            AlphaBox.Visibility = v;
        }
    }

    /// <summary>Show the eyedropper and "save color" buttons (only meaningful in the editor).</summary>
    public bool ShowImageTools
    {
        set
        {
            var v = value ? Visibility.Visible : Visibility.Collapsed;
            EyedropperButton.Visibility = v;
            SaveColorButton.Visibility = v;
        }
    }

    private void OnSaveColorClick(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(Color);

    public Color Color => ColorUtil.FromHsv(_h, _s, _v, _a);

    /// <summary>Open the picker for a color.</summary>
    public void Begin(Color color, string title)
    {
        TitleText.Text = title;
        _original = color;
        EyedropperButton.IsChecked = false;
        SetColorInternal(color, keepHue: false);
        OldSwatch.Background = ColorUtil.Brush(color);
        OldText.Foreground = ColorUtil.Brush(ColorUtil.Contrast(Opaque(color)));
    }

    /// <summary>Set the RGB from outside (eyedropper). Alpha is kept.</summary>
    public void SetRgbKeepAlpha(Color c)
    {
        SetColorInternal(Color.FromArgb(_a, c.R, c.G, c.B), keepHue: false);
        Changed();
    }

    public bool IsEyedropperActive => EyedropperButton.IsChecked == true;

    public void StopEyedropper() => EyedropperButton.IsChecked = false;

    private void SetColorInternal(Color c, bool keepHue)
    {
        var (h, s, v) = ColorUtil.ToHsv(c);
        if (!keepHue || s > 0.001 && v > 0.001) _h = h;
        if (!(keepHue && v <= 0.001)) _s = s;
        _v = v;
        _a = c.A;
        Refresh();
    }

    private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);

    private void Changed() => ColorChanged?.Invoke(Color);

    /// <summary>Push the current state to every control.</summary>
    private void Refresh()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var c = Color;
            var opaque = Opaque(c);
            SvHue.Background = ColorUtil.Brush(ColorUtil.FromHsv(_h, 1, 1));

            double w = SvArea.ActualWidth, h = SvArea.ActualHeight;
            Canvas.SetLeft(SvThumb, _s * w - SvThumb.Width / 2);
            Canvas.SetTop(SvThumb, (1 - _v) * h - SvThumb.Height / 2);
            SvThumb.Fill = ColorUtil.Brush(opaque);

            double hw = HueArea.ActualWidth;
            Canvas.SetLeft(HueThumb, _h / 360 * hw - HueThumb.Width / 2);

            double aw = AlphaArea.ActualWidth;
            Canvas.SetLeft(AlphaThumb, _a / 255.0 * aw - AlphaThumb.Width / 2);
            AlphaGradient.Background = new LinearGradientBrush(Color.FromArgb(0, c.R, c.G, c.B), opaque, 0);

            RSlider.Value = c.R;
            GSlider.Value = c.G;
            BSlider.Value = c.B;
            RSlider.Background = new LinearGradientBrush(Color.FromRgb(0, c.G, c.B), Color.FromRgb(255, c.G, c.B), 0);
            GSlider.Background = new LinearGradientBrush(Color.FromRgb(c.R, 0, c.B), Color.FromRgb(c.R, 255, c.B), 0);
            BSlider.Background = new LinearGradientBrush(Color.FromRgb(c.R, c.G, 0), Color.FromRgb(c.R, c.G, 255), 0);

            SetBox(RBox, c.R.ToString(CultureInfo.InvariantCulture));
            SetBox(GBox, c.G.ToString(CultureInfo.InvariantCulture));
            SetBox(BBox, c.B.ToString(CultureInfo.InvariantCulture));
            SetBox(HexBox, ColorUtil.ToHex6(c));
            SetBox(AlphaBox, ((int)Math.Round(_a / 255.0 * 100)).ToString(CultureInfo.InvariantCulture));

            NewSwatch.Background = ColorUtil.Brush(c);
            NewText.Foreground = ColorUtil.Brush(ColorUtil.Contrast(opaque));
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Don't overwrite a box the user is typing in (unless its value really differs).</summary>
    private static void SetBox(TextBox box, string text)
    {
        if (box.IsKeyboardFocusWithin)
        {
            // Keep the user's in-progress text when it already represents the same value.
            if (string.Equals(Normalize(box.Text), Normalize(text), StringComparison.OrdinalIgnoreCase)) return;
            if (box.Name == "HexBox") return;
        }
        if (box.Text != text) box.Text = text;
    }

    private static string Normalize(string s) => s.Trim().TrimStart('#').TrimStart('0');

    // ---------------- SV square ----------------

    private void OnSvDown(object sender, MouseButtonEventArgs e)
    {
        SvArea.Focus(); // take focus from the text boxes so they refresh
        SvArea.CaptureMouse();
        UpdateSv(e.GetPosition(SvArea));
        e.Handled = true;
    }

    private void OnSvMove(object sender, MouseEventArgs e)
    {
        if (SvArea.IsMouseCaptured) UpdateSv(e.GetPosition(SvArea));
    }

    private void OnSvUp(object sender, MouseButtonEventArgs e) => SvArea.ReleaseMouseCapture();

    private void UpdateSv(Point p)
    {
        _s = Math.Clamp(p.X / Math.Max(1, SvArea.ActualWidth), 0, 1);
        _v = 1 - Math.Clamp(p.Y / Math.Max(1, SvArea.ActualHeight), 0, 1);
        Refresh();
        Changed();
    }

    // ---------------- hue ----------------

    private void OnHueDown(object sender, MouseButtonEventArgs e)
    {
        HueArea.Focus(); // take focus from the text boxes so they refresh
        HueArea.CaptureMouse();
        UpdateHue(e.GetPosition(HueArea));
        e.Handled = true;
    }

    private void OnHueMove(object sender, MouseEventArgs e)
    {
        if (HueArea.IsMouseCaptured) UpdateHue(e.GetPosition(HueArea));
    }

    private void OnHueUp(object sender, MouseButtonEventArgs e) => HueArea.ReleaseMouseCapture();

    private void UpdateHue(Point p)
    {
        _h = Math.Clamp(p.X / Math.Max(1, HueArea.ActualWidth), 0, 1) * 360;
        if (_h >= 360) _h = 359.999;
        Refresh();
        Changed();
    }

    // ---------------- alpha ----------------

    private void OnAlphaDown(object sender, MouseButtonEventArgs e)
    {
        AlphaArea.Focus(); // take focus from the text boxes so they refresh
        AlphaArea.CaptureMouse();
        UpdateAlpha(e.GetPosition(AlphaArea));
        e.Handled = true;
    }

    private void OnAlphaMove(object sender, MouseEventArgs e)
    {
        if (AlphaArea.IsMouseCaptured) UpdateAlpha(e.GetPosition(AlphaArea));
    }

    private void OnAlphaUp(object sender, MouseButtonEventArgs e) => AlphaArea.ReleaseMouseCapture();

    private void UpdateAlpha(Point p)
    {
        _a = (byte)Math.Round(Math.Clamp(p.X / Math.Max(1, AlphaArea.ActualWidth), 0, 1) * 255);
        Refresh();
        Changed();
    }

    // ---------------- RGB / HEX / alpha inputs ----------------

    private void OnRgbSlider()
    {
        if (_updating) return;
        var c = Color.FromArgb(_a, (byte)Math.Round(RSlider.Value), (byte)Math.Round(GSlider.Value), (byte)Math.Round(BSlider.Value));
        SetColorInternal(c, keepHue: true);
        Changed();
    }

    private void OnRgbBox()
    {
        if (_updating) return;
        if (!TryByte(RBox.Text, out byte r) || !TryByte(GBox.Text, out byte g) || !TryByte(BBox.Text, out byte b)) return;
        var cur = Color;
        if (cur.R == r && cur.G == g && cur.B == b) return;
        SetColorInternal(Color.FromArgb(_a, r, g, b), keepHue: true);
        Changed();
    }

    private void OnHexBox()
    {
        if (_updating) return;
        string t = HexBox.Text.Trim();
        if (t.TrimStart('#').Length != 6) return;
        if (!ColorUtil.TryParse(t, out var c)) return;
        var cur = Color;
        if (cur.R == c.R && cur.G == c.G && cur.B == c.B) return;
        SetColorInternal(Color.FromArgb(_a, c.R, c.G, c.B), keepHue: true);
        Changed();
    }

    private void OnAlphaBox()
    {
        if (_updating) return;
        if (!int.TryParse(AlphaBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pct)) return;
        pct = Math.Clamp(pct, 0, 100);
        byte a = (byte)Math.Round(pct / 100.0 * 255);
        if (a == _a) return;
        _a = a;
        Refresh();
        Changed();
    }

    private static bool TryByte(string s, out byte b)
    {
        b = 0;
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return false;
        b = (byte)Math.Clamp(v, 0, 255);
        return true;
    }

    private static void DigitsOnly(object sender, TextCompositionEventArgs e)
    {
        foreach (char ch in e.Text)
            if (!char.IsDigit(ch)) { e.Handled = true; return; }
    }

    /// <summary>Up/Down arrows in a number box step the value (Shift = 10).</summary>
    private void ArrowStep(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down)) return;
        var box = (TextBox)sender;
        int max = box == AlphaBox ? 100 : 255;
        int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v);
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        v = Math.Clamp(v + (e.Key == Key.Up ? step : -step), 0, max);
        box.Text = v.ToString(CultureInfo.InvariantCulture);
        box.SelectAll();
        e.Handled = true;
    }

    // ---------------- buttons / keys ----------------

    private void OnOldClick(object sender, MouseButtonEventArgs e)
    {
        SetColorInternal(_original, keepHue: false);
        Changed();
    }

    private void OnEyedropperChecked(object sender, RoutedEventArgs e)
    {
        EyedropperButton.Background = (Brush)FindResource("AccentBrush");
        EyedropperHint.Visibility = Visibility.Visible;
        EyedropperToggled?.Invoke(true);
    }

    private void OnEyedropperUnchecked(object sender, RoutedEventArgs e)
    {
        EyedropperButton.ClearValue(BackgroundProperty);
        EyedropperHint.Visibility = Visibility.Collapsed;
        EyedropperToggled?.Invoke(false);
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Commit();

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancel();

    public void Commit()
    {
        StopEyedropper();
        Committed?.Invoke(Color);
    }

    public void Cancel()
    {
        StopEyedropper();
        Cancelled?.Invoke();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (IsEyedropperActive) StopEyedropper();
            else Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // Apply what was typed, then commit.
            OnHexBox();
            OnRgbBox();
            OnAlphaBox();
            Commit();
            e.Handled = true;
        }
    }
}
