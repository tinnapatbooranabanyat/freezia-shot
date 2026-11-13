using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SnapEdit.Native;
using SnapEdit.UI;

namespace SnapEdit.Capture;

/// <summary>
/// Full-screen frozen-screenshot overlay for ONE monitor. All selection state lives in
/// <see cref="CaptureSession"/>; this window only renders it in its own DIP space and forwards input.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly CaptureSession _session;
    private readonly ImageBrush _loupeBrush;
    private readonly Rectangle[] _handles = new Rectangle[8];

    public MonitorInfo Monitor { get; }

    public OverlayWindow(CaptureSession session, MonitorInfo monitor, PixelImage shot)
    {
        InitializeComponent();
        _session = session;
        Monitor = monitor;

        HintText.Text = Strings.OverlayHint;
        FullText.Text = Strings.OverlayFullScreen;
        AllText.Text = Strings.OverlayAllScreens;
        CancelText.Text = Strings.OverlayCancel;
        BtnEdit.ToolTip = Strings.ActionEdit + " (E)";
        BtnCopy.ToolTip = Strings.ActionCopy + " (Ctrl+C / Enter)";
        BtnSave.ToolTip = Strings.ActionSave + " (Ctrl+S)";
        BtnSaveAs.ToolTip = Strings.ActionSaveAs + " (Ctrl+Shift+S)";
        BtnCancel.ToolTip = Strings.ActionCancel + " (Esc)";

        // Frozen screenshot for this monitor.
        var local = monitor.Bounds.Intersect(shot.Bounds).Offset(-shot.OriginX, -shot.OriginY);
        if (!local.IsEmpty)
        {
            var crop = new CroppedBitmap(shot.Source, new Int32Rect(local.X, local.Y, local.Width, local.Height));
            crop.Freeze();
            Shot.Source = crop;
        }

        _loupeBrush = new ImageBrush(shot.Source)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
            TileMode = TileMode.None,
        };
        RenderOptions.SetBitmapScalingMode(_loupeBrush, BitmapScalingMode.NearestNeighbor);
        LoupeImage.Fill = _loupeBrush;

        for (int i = 0; i < 8; i++)
        {
            _handles[i] = new Rectangle
            {
                Fill = Brushes.White,
                Stroke = (Brush)FindResource("AccentBrush"),
                Visibility = Visibility.Collapsed,
            };
            Handles.Children.Add(_handles[i]);
        }

        // Rough initial placement; the exact physical bounds are applied once the HWND exists.
        Left = monitor.Bounds.X / monitor.Scale;
        Top = monitor.Bounds.Y / monitor.Scale;
        Width = monitor.Bounds.Width / monitor.Scale;
        Height = monitor.Bounds.Height / monitor.Scale;

        SourceInitialized += (_, _) =>
        {
            WindowHelper.MakeToolWindow(this);
            ApplyBounds();
        };
        Loaded += (_, _) => { ApplyBounds(); _session.RefreshAll(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { ApplyBounds(); _session.RefreshAll(); });
        SizeChanged += (_, _) => _session.RefreshAll();

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseRightButtonUp += (_, e) => { e.Handled = true; _session.RightClick(); };
        LostMouseCapture += (_, _) => _session.MouseCaptureLost();
    }

    private void ApplyBounds() => WindowHelper.SetPhysicalBounds(this, Monitor.Bounds);

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public void FocusOverlay()
    {
        try
        {
            NativeMethods.ForceForeground(Handle);
            Activate();
            Focus();
        }
        catch
        {
            // keys are routed through the global hook anyway
        }
    }

    // ---------------- coordinate mapping ----------------

    /// <summary>Physical pixels per DIP in this window.</summary>
    private double K => Root.ActualWidth > 0 ? Monitor.Bounds.Width / Root.ActualWidth : Monitor.Scale;

    private double ToLocalX(double px) => (px - Monitor.Bounds.X) / K;
    private double ToLocalY(double py) => (py - Monitor.Bounds.Y) / K;

    private Rect ToLocal(PxRect r) => new(ToLocalX(r.X), ToLocalY(r.Y), r.Width / K, r.Height / K);

    // ---------------- input ----------------

    private static bool Shift => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

    /// <summary>Physical screen position of a mouse event inside this window (exact at event time).</summary>
    private (int X, int Y) EventPosition(MouseEventArgs e)
    {
        var p = e.GetPosition(Root);
        double k = K;
        return ((int)Math.Floor(Monitor.Bounds.X + p.X * k), (int)Math.Floor(Monitor.Bounds.Y + p.Y * k));
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var (x, y) = EventPosition(e);
        CaptureMouse();
        _session.MouseDown(x, y, Shift, e.ClickCount);
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Root);
        bool inside = p.X >= 0 && p.Y >= 0 && p.X < Root.ActualWidth && p.Y < Root.ActualHeight;
        var (x, y) = inside ? EventPosition(e) : Monitors.CursorPosition();
        _session.MouseMove(x, y, Shift);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Root);
        bool inside = p.X >= 0 && p.Y >= 0 && p.X < Root.ActualWidth && p.Y < Root.ActualHeight;
        var (x, y) = inside ? EventPosition(e) : Monitors.CursorPosition();
        _session.MouseUp(x, y, Shift); // before releasing capture (LostMouseCapture would end the drag too)
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnEdit(object sender, RoutedEventArgs e) => _session.Finish(CaptureOutcome.Edit);
    private void OnCopy(object sender, RoutedEventArgs e) => _session.Finish(CaptureOutcome.Copy);
    private void OnSave(object sender, RoutedEventArgs e) => _session.Finish(CaptureOutcome.Save);
    private void OnSaveAs(object sender, RoutedEventArgs e) => _session.Finish(CaptureOutcome.SaveAs);
    private void OnCancel(object sender, RoutedEventArgs e) => _session.Finish(CaptureOutcome.Cancel);
    private void OnFullScreen(object sender, RoutedEventArgs e) => _session.FullScreen(Monitor);
    private void OnAllScreens(object sender, RoutedEventArgs e) => _session.Finish(CaptureOutcome.AllScreens);

    // ---------------- rendering ----------------

    public void SetCursorShape(Cursor c)
    {
        if (Cursor != c) Cursor = c;
    }

    public void Render(CaptureSession s)
    {
        double w = Root.ActualWidth, h = Root.ActualHeight;
        if (w <= 0 || h <= 0) return;
        double k = K;
        double onePx = 1.0 / k;

        // ---- dim + selection ----
        var full = new RectangleGeometry(new Rect(0, 0, w, h));
        PxRect selOnMon = s.HasSelection ? s.Selection.Intersect(Monitor.Bounds) : default;
        if (s.HasSelection && !selOnMon.IsEmpty)
        {
            Rect sel = ToLocal(s.Selection);
            var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            group.Children.Add(full);
            group.Children.Add(new RectangleGeometry(sel));
            Dim.Data = group;

            SelBorder.Visibility = Visibility.Visible;
            SelBorder.StrokeThickness = onePx;
            Canvas.SetLeft(SelBorder, sel.X - onePx);
            Canvas.SetTop(SelBorder, sel.Y - onePx);
            SelBorder.Width = sel.Width + 2 * onePx;
            SelBorder.Height = sel.Height + 2 * onePx;

            bool showHandles = s.Mode != DragMode.Creating;
            double hs = 7, half = hs / 2;
            double[] xs = { sel.Left, sel.Left + sel.Width / 2, sel.Right };
            double[] ys = { sel.Top, sel.Top + sel.Height / 2, sel.Bottom };
            // order matches SelectionHandle enum: NW, N, NE, E, SE, S, SW, W
            (int, int)[] idx = { (0, 0), (1, 0), (2, 0), (2, 1), (2, 2), (1, 2), (0, 2), (0, 1) };
            for (int i = 0; i < 8; i++)
            {
                var r = _handles[i];
                r.Visibility = showHandles ? Visibility.Visible : Visibility.Collapsed;
                r.Width = r.Height = hs;
                r.StrokeThickness = onePx;
                Canvas.SetLeft(r, xs[idx[i].Item1] - half);
                Canvas.SetTop(r, ys[idx[i].Item2] - half);
            }
        }
        else
        {
            Dim.Data = full;
            SelBorder.Visibility = Visibility.Collapsed;
            foreach (var r in _handles) r.Visibility = Visibility.Collapsed;
        }

        // ---- size label (on the monitor holding the selection's top-left) ----
        if (s.HasSelection && s.LabelMonitor == Monitor)
        {
            Rect sel = ToLocal(s.Selection);
            SizeText.Text = $"{s.Selection.Width} × {s.Selection.Height}";
            SizeLabel.Visibility = Visibility.Visible;
            SizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var ls = SizeLabel.DesiredSize;
            double lx = Math.Clamp(sel.X, 4, Math.Max(4, w - ls.Width - 4));
            double ly = sel.Y - ls.Height - 6;
            if (ly < 4) ly = sel.Y + 6;
            Canvas.SetLeft(SizeLabel, lx);
            Canvas.SetTop(SizeLabel, ly);
        }
        else
        {
            SizeLabel.Visibility = Visibility.Collapsed;
        }

        // ---- action bar ----
        if (s.HasSelection && s.Mode == DragMode.None && s.ActionMonitor == Monitor)
        {
            ActionBar.Visibility = Visibility.Visible;
            ActionBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var bs = ActionBar.DesiredSize;
            Rect sel = ToLocal(s.Selection);
            double bx = sel.Right - bs.Width;
            double by = sel.Bottom + 8;
            if (by + bs.Height > h - 4)
            {
                by = sel.Top - bs.Height - 8;                       // above
                if (by < 4) by = sel.Bottom - bs.Height - 8;       // inside, bottom
            }
            bx = Math.Clamp(bx, 4, Math.Max(4, w - bs.Width - 4));
            by = Math.Clamp(by, 4, Math.Max(4, h - bs.Height - 4));
            Canvas.SetLeft(ActionBar, bx);
            Canvas.SetTop(ActionBar, by);
        }
        else
        {
            ActionBar.Visibility = Visibility.Collapsed;
        }

        // ---- top bar: hidden while dragging; moves to the bottom if the selection is under it ----
        TopBar.Visibility = s.Mode == DragMode.None ? Visibility.Visible : Visibility.Collapsed;
        if (TopBar.Visibility == Visibility.Visible)
        {
            bool overlaps = false;
            if (s.HasSelection && !selOnMon.IsEmpty)
            {
                Rect sel = ToLocal(s.Selection);
                overlaps = sel.Top < 90 && sel.Bottom > 0 && sel.Left < w / 2 + 300 && sel.Right > w / 2 - 300;
            }
            TopBar.VerticalAlignment = overlaps ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        }

        // ---- loupe ----
        var (cx, cy) = s.Cursor;
        bool cursorHere = Monitor.Bounds.Contains(cx, cy);
        if (cursorHere && s.ShowLoupe)
        {
            int ix = cx - s.Shot.OriginX, iy = cy - s.Shot.OriginY;
            _loupeBrush.Viewbox = new Rect(ix - 7, iy - 7, 15, 15);
            var c = s.Shot.GetPixel(ix, iy);
            LoupeSwatch.Background = new SolidColorBrush(c);
            LoupeHex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            LoupePos.Text = $"X: {cx}   Y: {cy}";
            Loupe.Visibility = Visibility.Visible;

            double lx = ToLocalX(cx) + 22, ly = ToLocalY(cy) + 22;
            const double lw = 136, lh = 180;
            if (lx + lw > w - 4) lx = ToLocalX(cx) - lw - 22;
            if (ly + lh > h - 4) ly = ToLocalY(cy) - lh - 22;
            Canvas.SetLeft(Loupe, Math.Max(4, lx));
            Canvas.SetTop(Loupe, Math.Max(4, ly));
        }
        else
        {
            Loupe.Visibility = Visibility.Collapsed;
        }
    }
}
