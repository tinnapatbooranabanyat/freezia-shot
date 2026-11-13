using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Freezia.Capture;
using Freezia.Core;

namespace Freezia.UI;

/// <summary>
/// A screenshot floating above all windows ("pin to screen"). Shown at its real pixel size where it was
/// captured. Drag = move, wheel = zoom, Ctrl + wheel = opacity, double-click / Esc = close.
/// </summary>
public sealed class PinWindow : Window
{
    private readonly PixelImage _image;
    private readonly Action<PixelImage>? _edit;
    private readonly Func<PixelImage, bool>? _copy;
    private readonly Action<PixelImage>? _save;
    private readonly Border _frame;
    private readonly Border _badge;
    private readonly TextBlock _badgeText = new() { FontSize = 12, Foreground = Brushes.White };
    private double _zoom = 1.0;
    private PxRect _placed;

    public PinWindow(PixelImage image, Action<PixelImage>? edit, Func<PixelImage, bool>? copy, Action<PixelImage>? save)
    {
        _image = image;
        _edit = edit;
        _copy = copy;
        _save = save;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Title = "Freezia pin";
        SetResourceReference(FontFamilyProperty, "UiFont");

        var img = new Image { Source = image.Source, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        _frame = new Border { BorderThickness = new Thickness(1), Child = img };
        _frame.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");

        _badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x21, 0x27)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
            Opacity = 0,
            IsHitTestVisible = false,
            Child = _badgeText,
        };
        var root = new Grid();
        root.Children.Add(_frame);
        root.Children.Add(_badge);
        Content = root;
        ContextMenu = BuildMenu();

        SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this);
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { Close(); return; }
            try { DragMove(); } catch (InvalidOperationException) { }
            _placed = CurrentBounds();
        };
        MouseWheel += OnWheel;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) _copy?.Invoke(_image);
        };
    }

    /// <summary>Show at the capture position (physical pixels), 1:1.</summary>
    public void ShowAt(int x, int y)
    {
        _placed = new PxRect(x, y, _image.Width + 2, _image.Height + 2);
        Left = -32000;
        Top = -32000;
        Width = 10;
        Height = 10;
        Show();
        Place();
        Dispatcher.BeginInvoke(Place, System.Windows.Threading.DispatcherPriority.Loaded);
        Activate();
    }

    /// <summary>Show centered on the monitor under the mouse (e.g. from the editor or history).</summary>
    public void ShowCentered()
    {
        var wa = Monitors.FromCursor().WorkArea;
        int w = Math.Min(_image.Width, wa.Width - 40), h = Math.Min(_image.Height, wa.Height - 40);
        if (w < _image.Width || h < _image.Height) _zoom = Math.Min((double)w / _image.Width, (double)h / _image.Height);
        int pw = (int)(_image.Width * _zoom) + 2, ph = (int)(_image.Height * _zoom) + 2;
        ShowAt(wa.X + (wa.Width - pw) / 2, wa.Y + (wa.Height - ph) / 2);
    }

    private PxRect CurrentBounds()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Native.NativeMethods.GetWindowRect(hwnd, out var r)) return _placed;
        return new PxRect(r.Left, r.Top, _placed.Width, _placed.Height);
    }

    private void Place()
    {
        int w = (int)Math.Round(_image.Width * _zoom) + 2, h = (int)Math.Round(_image.Height * _zoom) + 2;
        _placed = new PxRect(_placed.X, _placed.Y, w, h);
        WindowHelper.SetPhysicalBounds(this, _placed);
        // DIP size of the content follows the window; the frame is exactly 1 device pixel so the
        // image itself stays pixel-for-pixel at 100 %
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Width = w / s;
        Height = h / s;
        _frame.BorderThickness = new Thickness(1 / s);
        RenderOptions.SetBitmapScalingMode((Image)_frame.Child,
            Math.Abs(_zoom - 1) < 0.001 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        _placed = CurrentBounds();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.2, 1.0);
            Badge($"{Math.Round(Opacity * 100)}%");
        }
        else
        {
            double old = _zoom;
            _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.1, 4.0);
            if (Math.Abs(_zoom - 1) < 0.04) _zoom = 1;
            // zoom around the mouse
            var p = e.GetPosition(this);
            double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double k = _zoom / old;
            int nx = (int)Math.Round(_placed.X + p.X * s * (1 - k));
            int ny = (int)Math.Round(_placed.Y + p.Y * s * (1 - k));
            _placed = new PxRect(nx, ny, _placed.Width, _placed.Height);
            Place();
            Badge($"{Math.Round(_zoom * 100)}%");
        }
        e.Handled = true;
    }

    private void Badge(string text)
    {
        _badgeText.Text = text;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000))));
        _badge.BeginAnimation(OpacityProperty, a);
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        void Add(string text, Action act)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }
        if (_copy != null) Add(Strings.ActionCopy, () => _copy(_image));
        if (_save != null) Add(Strings.ActionSave, () => _save(_image));
        if (_edit != null) Add(Strings.ActionEdit, () => { _edit(_image); Close(); });
        Add(Strings.PinActualSize, () => { _placed = CurrentBounds(); _zoom = 1; Place(); Badge("100%"); });
        Add(Strings.Close, Close);
        return menu;
    }
}
