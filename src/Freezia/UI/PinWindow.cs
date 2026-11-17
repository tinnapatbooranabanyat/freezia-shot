using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Freezia.Capture;
using Freezia.Core;
using Freezia.Native;

namespace Freezia.UI;

/// <summary>
/// A screenshot floating above all windows ("pin to screen"), shown at its real pixel size where it was
/// captured. Drag = move; drag an edge / corner = resize (keeps the aspect ratio, snaps to 100 %);
/// wheel = zoom around the mouse; Ctrl + wheel = opacity; Ctrl+0 / Ctrl+= / Ctrl+- = actual size / zoom;
/// double-click / Esc = close. Red / yellow / green buttons (top-left, on hover) close, minimize to a small
/// thumbnail (click it to restore) and toggle "fit the screen".
/// All sizes are computed in physical pixels, so 100 % is pixel-for-pixel on any DPI.
/// </summary>
public sealed class PinWindow : Window
{
    private const double MaxZoom = 4.0, MinZoom = 0.1;
    private const int EdgePx = 6;            // resize zone inside the window edge (device pixels)
    private const double ThumbHeightDip = 56;

    private readonly PixelImage _image;
    private readonly Action<PixelImage>? _edit;
    private readonly Func<PixelImage, bool>? _copy;
    private readonly Action<PixelImage>? _save;
    private readonly Grid _root = new();
    private readonly Border _frame;
    private readonly Image _img;
    private readonly Border _badge;
    private readonly TextBlock _badgeText = new() { FontSize = 12, Foreground = Brushes.White };
    private readonly TrafficLights _lights;
    private readonly Popup _lightsPopup;
    private double _zoom = 1.0;
    private int _borderPx = 1;
    private PxRect _placed;

    // resize by dragging an edge / corner
    private int _resizeEdges; // bit 1 = left, 2 = right, 4 = top, 8 = bottom
    private PxRect _resizeStart;

    // minimize / fit-to-screen
    private bool _minimized, _fitted;
    private double _restoreZoom;
    private PxRect _restoreBounds;
    private System.Windows.Threading.DispatcherTimer? _anim;

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

        _img = new Image { Source = image.Source, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
        _frame = new Border { BorderThickness = new Thickness(1), Child = _img };
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

        _lights = new TrafficLights(Close, Minimize, ToggleFit,
            () => Strings.PinClose, () => Strings.PinMinimize, () => _fitted ? Strings.PinRestore : Strings.PinFit)
        {
            Margin = new Thickness(6),
            Opacity = 0,
        };
        _lights.MouseLeave += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!IsMouseOver && !_lights.IsMouseOver) ShowLights(false);
        }, System.Windows.Threading.DispatcherPriority.Input);
        _lightsPopup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Top,
            PlacementTarget = _frame,
            VerticalOffset = -2,
        };

        _root.Children.Add(_frame);
        _root.Children.Add(_badge);
        _root.Children.Add(_lights);
        Content = _root;
        ContextMenu = BuildMenu();

        SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this);
        PreviewMouseLeftButtonDown += OnPreviewDown;
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        LostMouseCapture += (_, _) => _resizeEdges = 0;
        MouseWheel += OnWheel;
        MouseEnter += (_, _) => ShowLights(true);
        MouseLeave += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!IsMouseOver && !_lights.IsMouseOver) ShowLights(false);
        }, System.Windows.Threading.DispatcherPriority.Input);
        LocationChanged += (_, _) => NudgePopup();
        // dragged onto a monitor with another DPI: keep the physical size (100 % stays pixel-exact)
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { _placed = CurrentBounds(); Place(); });
        KeyDown += OnKey;
        Closed += (_, _) => _lightsPopup.IsOpen = false;
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

    private double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private PxRect CurrentBounds()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var r)) return _placed;
        return new PxRect(r.Left, r.Top, _placed.Width, _placed.Height);
    }

    /// <summary>Window size in device pixels for a zoom (image + frame).</summary>
    private (int W, int H) SizeFor(double zoom) =>
        ((int)Math.Round(_image.Width * zoom) + 2 * _borderPx, (int)Math.Round(_image.Height * zoom) + 2 * _borderPx);

    private void Place()
    {
        var (w, h) = SizeFor(_zoom);
        _placed = new PxRect(_placed.X, _placed.Y, w, h);
        WindowHelper.SetPhysicalBounds(this, _placed);
        // DIP size of the content follows the window; the frame is exactly 1 (or 2) device pixels so the
        // image itself stays pixel-for-pixel at 100 %
        double s = Dpi;
        Width = w / s;
        Height = h / s;
        _frame.BorderThickness = new Thickness(_borderPx / s);
        RenderOptions.SetBitmapScalingMode(_img,
            Math.Abs(_zoom - 1) < 0.001 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        UpdateLightsHost();
    }

    /// <summary>Smallest zoom: the pin stays at least a few pixels on its short side.</summary>
    private double MinZoomForImage => Math.Min(1.0, Math.Max(MinZoom, 16.0 / Math.Max(1, Math.Min(_image.Width, _image.Height))));

    private static double SnapToActual(double z) => Math.Abs(z - 1) < 0.04 ? 1 : z;

    // ================================================================ mouse: move / resize

    private int EdgesAt(Point p)
    {
        if (_minimized) return 0;
        double s = Dpi, e = EdgePx / s;
        double w = ActualWidth, h = ActualHeight;
        int edges = 0;
        if (p.X <= e) edges |= 1;
        else if (p.X >= w - e) edges |= 2;
        if (p.Y <= e) edges |= 4;
        else if (p.Y >= h - e) edges |= 8;
        return edges;
    }

    private static Cursor? CursorFor(int edges) => edges switch
    {
        1 or 2 => Cursors.SizeWE,
        4 or 8 => Cursors.SizeNS,
        5 or 10 => Cursors.SizeNWSE,
        6 or 9 => Cursors.SizeNESW,
        _ => null,
    };

    private void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (_lights.IsMouseOver) return; // the buttons handle their own clicks
        int edges = EdgesAt(e.GetPosition(this));
        if (edges == 0) return;
        StopAnimation();
        _placed = CurrentBounds();
        _resizeStart = _placed;
        _resizeEdges = edges;
        CaptureMouse();
        e.Handled = true; // no DragMove, no double-click close
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        if (_minimized)
        {
            // drag = move the thumbnail; a click without moving = restore
            var before = CurrentBounds();
            try { DragMove(); } catch (InvalidOperationException) { }
            var after = CurrentBounds();
            if (Math.Abs(after.X - before.X) < 3 && Math.Abs(after.Y - before.Y) < 3) Restore();
            else _placed = after;
            return;
        }
        if (e.ClickCount == 2) { Close(); return; }
        try { DragMove(); } catch (InvalidOperationException) { }
        _placed = CurrentBounds();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_resizeEdges == 0)
        {
            Cursor = _lights.IsMouseOver ? null : CursorFor(EdgesAt(e.GetPosition(this)));
            return;
        }
        var (cx, cy) = Monitors.CursorPosition();
        ResizeTo(cx, cy, _resizeEdges);
    }

    /// <summary>Resize from an edge / corner to a screen point, keeping the aspect ratio and the opposite side.</summary>
    private void ResizeTo(int cx, int cy, int edges)
    {
        var r = _resizeStart;
        int bw = 2 * _borderPx;
        double zx = (edges & 3) == 0 ? double.NaN
            : (edges & 1) != 0 ? (r.Right - cx - bw) / (double)_image.Width : (cx - r.X - bw) / (double)_image.Width;
        double zy = (edges & 12) == 0 ? double.NaN
            : (edges & 4) != 0 ? (r.Bottom - cy - bw) / (double)_image.Height : (cy - r.Y - bw) / (double)_image.Height;
        double z = double.IsNaN(zx) ? zy : double.IsNaN(zy) ? zx : Math.Max(zx, zy); // corners follow the farther side
        z = SnapToActual(Math.Clamp(z, MinZoomForImage, MaxZoom));
        _zoom = z;
        _fitted = false;
        var (w, h) = SizeFor(z);
        int x = (edges & 1) != 0 ? r.Right - w : r.X;
        int y = (edges & 4) != 0 ? r.Bottom - h : r.Y;
        _placed = new PxRect(x, y, w, h);
        Place();
        Badge($"{Math.Round(_zoom * 100)}%");
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizeEdges == 0) return;
        _resizeEdges = 0;
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (_minimized) return;
        _placed = CurrentBounds();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.2, 1.0);
            Badge($"{Math.Round(Opacity * 100)}%");
            return;
        }
        var p = e.GetPosition(this);
        double s = Dpi;
        ZoomAround(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), p.X * s, p.Y * s);
    }

    /// <summary>Zoom keeping the point (device pixels from the window's top-left) under the mouse.</summary>
    private void ZoomAround(double zoom, double ax, double ay)
    {
        StopAnimation();
        double old = _zoom;
        _zoom = SnapToActual(Math.Clamp(zoom, MinZoomForImage, MaxZoom));
        _fitted = false;
        double k = _zoom / old;
        int nx = (int)Math.Round(_placed.X + ax * (1 - k));
        int ny = (int)Math.Round(_placed.Y + ay * (1 - k));
        _placed = new PxRect(nx, ny, _placed.Width, _placed.Height);
        Place();
        Badge($"{Math.Round(_zoom * 100)}%");
    }

    private void ActualSize()
    {
        if (_minimized) Restore();
        StopAnimation();
        _placed = CurrentBounds();
        _zoom = 1;
        _fitted = false;
        Place();
        Badge("100%");
    }

    // ================================================================ keys

    private void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.C when ctrl: _copy?.Invoke(_image); break;
            case Key.D0 or Key.NumPad0 when ctrl: ActualSize(); break;
            case Key.OemPlus or Key.Add when ctrl:
                if (!_minimized) { _placed = CurrentBounds(); ZoomAround(_zoom * 1.1, _placed.Width / 2.0, _placed.Height / 2.0); }
                break;
            case Key.OemMinus or Key.Subtract when ctrl:
                if (!_minimized) { _placed = CurrentBounds(); ZoomAround(_zoom / 1.1, _placed.Width / 2.0, _placed.Height / 2.0); }
                break;
            default: return;
        }
        e.Handled = true;
    }

    // ================================================================ minimize / fit

    /// <summary>Shrink to a small thumbnail with an accent frame at the same top-left corner.</summary>
    public void Minimize()
    {
        if (_minimized) return;
        StopAnimation();
        _placed = CurrentBounds();
        _restoreZoom = _zoom;
        _restoreBounds = _placed;
        _minimized = true;
        _borderPx = 2;
        ShowLights(false);
        double thumbPx = ThumbHeightDip * Dpi;
        double target = Math.Min(_zoom, thumbPx / _image.Height);
        AnimateTo(target, _placed.X, _placed.Y);
    }

    /// <summary>Back to the size and place before minimizing / fitting.</summary>
    public void Restore()
    {
        if (!_minimized && !_fitted) return;
        StopAnimation();
        _minimized = false;
        _fitted = false;
        _borderPx = 1;
        AnimateTo(_restoreZoom, _restoreBounds.X, _restoreBounds.Y);
    }

    /// <summary>Toggle: fit the work area of the pin's monitor (up to 400 %), or back to the previous size.</summary>
    public void ToggleFit()
    {
        if (_fitted)
        {
            Restore();
            return;
        }
        if (_minimized)
        {
            _minimized = false;
            _borderPx = 1;
        }
        else
        {
            _placed = CurrentBounds();
            _restoreZoom = _zoom;
            _restoreBounds = _placed;
        }
        var mon = Monitors.FromPoint(_placed.X + _placed.Width / 2, _placed.Y + _placed.Height / 2);
        var wa = mon.WorkArea;
        int margin = (int)Math.Round(24 * mon.Scale);
        double z = Math.Min((wa.Width - 2.0 * margin - 2) / _image.Width, (wa.Height - 2.0 * margin - 2) / _image.Height);
        z = Math.Clamp(z, MinZoomForImage, MaxZoom);
        var (w, h) = ((int)Math.Round(_image.Width * z) + 2, (int)Math.Round(_image.Height * z) + 2);
        _fitted = true;
        AnimateTo(z, wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2);
    }

    /// <summary>~150 ms animation of zoom and position (each frame placed in device pixels).</summary>
    private void AnimateTo(double zoom, int x, int y)
    {
        StopAnimation();
        double z0 = _zoom;
        int x0 = _placed.X, y0 = _placed.Y;
        var start = DateTime.Now;
        _anim = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(15),
        };
        _anim.Tick += (_, _) =>
        {
            double t = Math.Min(1, (DateTime.Now - start).TotalMilliseconds / 150);
            double k = 1 - Math.Pow(1 - t, 3); // ease out
            _zoom = z0 + (zoom - z0) * k;
            _placed = new PxRect((int)Math.Round(x0 + (x - x0) * k), (int)Math.Round(y0 + (y - y0) * k), 0, 0);
            if (t >= 1)
            {
                _zoom = zoom;
                _placed = new PxRect(x, y, 0, 0);
                StopAnimation();
            }
            Place();
        };
        _anim.Start();
    }

    private void StopAnimation()
    {
        _anim?.Stop();
        _anim = null;
    }

    // ================================================================ traffic lights

    /// <summary>Very small pins: the buttons float above the image instead of covering it.</summary>
    private bool LightsOutside => ActualWidth < 80 || ActualHeight < 40;

    private void UpdateLightsHost()
    {
        bool outside = LightsOutside && !_minimized;
        bool inPopup = _lightsPopup.Child == _lights;
        if (outside == inPopup) return;
        if (outside)
        {
            _root.Children.Remove(_lights);
            _lights.Margin = new Thickness(0);
            _lightsPopup.Child = _lights;
        }
        else
        {
            _lightsPopup.IsOpen = false;
            _lightsPopup.Child = null;
            _lights.Margin = new Thickness(6);
            _root.Children.Add(_lights);
        }
    }

    private void ShowLights(bool on)
    {
        if (_minimized) on = false;
        UpdateLightsHost();
        if (_lightsPopup.Child == _lights) _lightsPopup.IsOpen = on;
        _lights.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 120 : 180)));
        _lights.IsHitTestVisible = on;
    }

    private void NudgePopup()
    {
        if (!_lightsPopup.IsOpen) return;
        double h = _lightsPopup.HorizontalOffset;
        _lightsPopup.HorizontalOffset = h + 0.01;
        _lightsPopup.HorizontalOffset = h;
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
        MenuItem Add(string text, Action act)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
            return mi;
        }
        if (_copy != null) Add(Strings.ActionCopy, () => _copy(_image));
        if (_save != null) Add(Strings.ActionSave, () => _save(_image));
        if (_edit != null) Add(Strings.ActionEdit, () => { _edit(_image); Close(); });
        Add(Strings.PinActualSize, ActualSize);
        var min = Add(Strings.PinMinimizeMenu, Minimize);
        var fit = Add(Strings.PinFitMenu, ToggleFit);
        Add(Strings.Close, Close);
        menu.Opened += (_, _) =>
        {
            min.IsEnabled = !_minimized;
            fit.Header = _fitted ? Strings.PinRestore : Strings.PinFitMenu;
        };
        return menu;
    }
}
