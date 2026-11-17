using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Freezia.Capture;
using Freezia.Core;

namespace Freezia.UI;

/// <summary>Small non-activating notification at the bottom-right of the monitor under the cursor.</summary>
public partial class ToastWindow : Window
{
    private static ToastWindow? _current;
    private readonly DispatcherTimer _timer;
    private readonly string? _revealPath;
    private readonly Action? _onClick;
    private readonly TrafficLights _lights;
    private bool _closing;
    private bool _kept; // yellow button: stays until closed

    private ToastWindow(string title, string? message, string? revealPath, BitmapSource? thumb, bool error,
                        Action? onClick, double? seconds)
    {
        InitializeComponent();
        _revealPath = revealPath;
        _onClick = onClick;
        TitleText.Text = title;
        if (!string.IsNullOrEmpty(message))
        {
            MessageText.Text = message;
            MessageText.Visibility = Visibility.Visible;
        }
        if (thumb != null)
        {
            Thumb.Source = thumb;
            Thumb.Visibility = Visibility.Visible;
            if (thumb.PixelWidth <= 2) // a color swatch
            {
                Thumb.Width = Thumb.Height = 34;
                Thumb.Stretch = Stretch.Fill;
                RenderOptions.SetBitmapScalingMode(Thumb, BitmapScalingMode.NearestNeighbor);
            }
        }
        if (error)
        {
            IconGlyph.Text = ""; // Error
            IconGlyph.Foreground = (Brush)FindResource("DangerBrush");
            IconBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x5C, 0x5C));
        }
        if (revealPath == null && onClick == null) Card.Cursor = Cursors.Arrow;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds ?? (error ? 4 : 3)) };
        _timer.Tick += (_, _) => FadeOut();

        // red = close, yellow = keep it on screen, green = open the folder (or the toast's own action)
        Action? green = revealPath != null ? () => { RevealInExplorer(revealPath); CloseNow(); }
            : onClick != null ? () => { CloseNow(); onClick(); }
            : null;
        _lights = new TrafficLights(CloseNow, Keep, green,
            () => Strings.Close, () => Strings.ToastKeep, () => revealPath != null ? Strings.ToastOpenFolder : Strings.ToastOpen)
        {
            Margin = new Thickness(8, 6, 0, 0),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        Root.Children.Add(_lights);

        SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this, noActivate: true);
        Loaded += OnLoaded;
        MouseEnter += (_, _) =>
        {
            _timer.Stop();
            FadeLights(true);
        };
        MouseLeave += (_, _) =>
        {
            FadeLights(false);
            if (!_closing && !_kept) _timer.Start();
        };
        MouseLeftButtonUp += OnClick;
    }

    public static void ShowToast(string title, string? message = null, string? revealPath = null,
                                 BitmapSource? thumb = null, bool error = false,
                                 Action? onClick = null, double? seconds = null, string? glyph = null)
    {
        try
        {
            _current?.CloseNow();
            var t = new ToastWindow(title, message, revealPath, thumb, error, onClick, seconds);
            if (glyph != null) t.IconGlyph.Text = glyph;
            _current = t;
            t.Show();
        }
        catch (Exception ex)
        {
            Logger.Error("Toast failed", ex);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Place at bottom-right of the work area of the monitor under the cursor (physical px).
        var mon = Monitors.FromCursor();
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int w = (int)Math.Ceiling(ActualWidth * s), h = (int)Math.Ceiling(ActualHeight * s);
        var wa = mon.WorkArea;
        WindowHelper.SetPhysicalBounds(this, new PxRect(wa.Right - w, wa.Bottom - h, w, h));
        // DPI may have changed after moving: re-measure once more.
        Dispatcher.BeginInvoke(() =>
        {
            double s2 = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int w2 = (int)Math.Ceiling(ActualWidth * s2), h2 = (int)Math.Ceiling(ActualHeight * s2);
            WindowHelper.SetPhysicalBounds(this, new PxRect(wa.Right - w2, wa.Bottom - h2, w2, h2));
        }, DispatcherPriority.Loaded);

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
        BeginAnimation(OpacityProperty, fade);
        var slide = new TranslateTransform(0, 12);
        Card.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _timer.Start();
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (_revealPath != null) RevealInExplorer(_revealPath);
        CloseNow();
        _onClick?.Invoke();
    }

    public static void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error("Reveal in explorer failed", ex);
        }
    }

    /// <summary>Yellow: the toast no longer closes by itself.</summary>
    private void Keep()
    {
        _kept = true;
        _timer.Stop();
        _lights.SetTip(1, Strings.ToastKept);
    }

    private void FadeLights(bool on)
    {
        _lights.IsHitTestVisible = on;
        _lights.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 120 : 180)));
    }

    private void FadeOut()
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        var anim = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220));
        anim.Completed += (_, _) => CloseNow();
        BeginAnimation(OpacityProperty, anim);
    }

    private void CloseNow()
    {
        _timer.Stop();
        _closing = true;
        if (_current == this) _current = null;
        try { Close(); } catch { /* already closed */ }
    }
}
