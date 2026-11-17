using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Freezia.Core;
using Freezia.Editor;
using Freezia.Native;
using Freezia.UI;

namespace Freezia.Capture;

/// <summary>
/// Lightshot-style drawing on the capture overlay.
/// <para>
/// The drawings live in an <see cref="EditSurface"/> whose image is the whole frozen screenshot, so they are
/// anchored to the screen (moving / resizing the frame does not move them; the frame only decides what is
/// shown and exported). The surface sits in an opaque, borderless edit window laid exactly over the selection
/// (1 image pixel = 1 device pixel); its background is the same screenshot, so it looks transparent but is
/// GPU-rendered. While the frame is dragged the edit window is cloaked and the overlays show the drawings
/// through a VisualBrush instead, so nothing has to follow the mouse.
/// </para>
/// <para>
/// With no tool selected the edit window lets the mouse through (WM_NCHITTEST = HTTRANSPARENT) to the overlay,
/// whose selection logic moves / resizes the frame. The handles of the frame always go to the overlay.
/// </para>
/// </summary>
public sealed class CaptureEditor
{
    private readonly CaptureSession _session;
    private readonly SettingsService _settings;
    private readonly EditHostWindow _host;
    private readonly ToolbarWindow _toolsWindow, _styleWindow, _actionsWindow;
    private readonly ToolStrip _tools;
    private readonly StyleBar _styles;
    private readonly ActionBarControl _actions;
    private bool _closed;

    public EditSurface Surface { get; }

    /// <summary>The edit window is shown (not cloaked): the overlays don't need to draw the drawings.</summary>
    public bool HostVisible { get; private set; }

    /// <summary>Raised when text editing starts / ends (keyboard routing).</summary>
    public event Action<bool>? TextInputChanged;

    private static CaptureEditor? _current;
    private static bool _focusHandlersRegistered;

    /// <summary>
    /// Any text box of ours with keyboard focus (drawing text, the color picker's HEX / RGB boxes) needs
    /// every key: the keyboard hook must stop routing keys to the capture while it has focus.
    /// </summary>
    private static void RegisterFocusHandlers()
    {
        if (_focusHandlersRegistered) return;
        _focusHandlersRegistered = true;
        EventManager.RegisterClassHandler(typeof(TextBoxBase), UIElement.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((_, _) => _current?.UpdateTextInput()), true);
        EventManager.RegisterClassHandler(typeof(TextBoxBase), UIElement.LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((_, _) => _current?.Dispatcher.BeginInvoke(() => _current?.UpdateTextInput())), true);
    }

    private Dispatcher Dispatcher => _host.Dispatcher;
    private bool _textInput;

    private void UpdateTextInput()
    {
        bool on = Surface.IsEditingText || Keyboard.FocusedElement is TextBoxBase;
        if (on == _textInput) return;
        _textInput = on;
        TextInputChanged?.Invoke(on);
    }

    public CaptureEditor(CaptureSession session, SettingsService settings)
    {
        RegisterFocusHandlers();
        _current = this;
        _session = session;
        _settings = settings;
        var es = settings.Current.Editor;
        var style = new AnnotationStyle(
            ColorUtil.Parse(es.StrokeColor, StyleBar.Presets[0]),
            ColorUtil.Parse(es.FillColor, Color.FromArgb(0x66, 0xFF, 0xD6, 0x0A)),
            es.FillEnabled, es.Thickness, es.FontSize);
        Surface = new EditSurface(session.Shot, style);
        RenderOptions.SetBitmapScalingMode(Surface.BaseImage, BitmapScalingMode.NearestNeighbor);

        _host = new EditHostWindow(this, Surface);
        Surface.InputElement = _host.InputArea;

        _tools = new ToolStrip { AllowNone = true, ShowCrop = false, ShowCommands = false };
        _tools.Orientation = Orientation.Vertical;
        _tools.Attach(Surface);
        _styles = new StyleBar();
        _styles.Attach(Surface, settings);
        _actions = new ActionBarControl();
        _actions.Attach(Surface);
        _actions.Requested += o => _session.Finish(o);

        _toolsWindow = new ToolbarWindow(_tools, activatable: false);
        _styleWindow = new ToolbarWindow(_styles, activatable: true); // the color picker's text boxes need focus
        _actionsWindow = new ToolbarWindow(_actions, activatable: false);

        Surface.StateChanged += () => { if (!_closed) _session.RefreshAll(); };
        Surface.CommandsChanged += () => { if (!_closed && _session.Selection.Width > 0) _session.RefreshOverlays(); };
        Surface.TextEditingChanged += OnTextEditing;
    }

    // ================================================================ layout

    /// <summary>Follow the session: edit window over the selection, toolbars next to it.</summary>
    private object? _layoutKey;

    public void Sync()
    {
        if (_closed) return;
        var s = _session;
        bool idle = s.HasSelection && s.Mode == DragMode.None;
        bool hostOn = idle && !s.IsCrossDpi && s.Selection.Width >= 2 && s.Selection.Height >= 2;
        // RefreshAll runs on every mouse move: only re-place windows when something relevant changed
        var key = (s.Selection, s.Mode, s.IsCrossDpi, Surface.Tool, Surface.Selected != null, Surface.IsEditingText);
        if (Equals(key, _layoutKey)) return;
        _layoutKey = key;

        if (hostOn)
        {
            _host.Place(s.Selection, s.Shot);
            if (!HostVisible) _host.Uncloak();
            HostVisible = true;
        }
        else
        {
            if (HostVisible) _host.Cloak();
            HostVisible = false;
            if (Surface.IsDragging) Surface.PointerCaptureLost();
        }
        if (!hostOn && Surface.Tool != EditorTool.None && s.IsCrossDpi) Surface.SetTool(EditorTool.None);

        if (!idle)
        {
            _toolsWindow.HideBar();
            _styleWindow.HideBar();
            _actionsWindow.HideBar();
            _styles.CommitPicker();
            return;
        }
        LayoutToolbars(hostOn);
    }

    private void LayoutToolbars(bool tools)
    {
        var s = _session;
        var sel = s.Selection;
        var mon = s.ActionMonitor ?? Monitors.FromPoint(sel.X, sel.Y);
        var area = mon.Bounds;
        int gap = (int)Math.Round(8 * mon.Scale), m = (int)Math.Round(4 * mon.Scale);

        // actions: below the selection, right-aligned (above / inside when there is no room)
        var (aw, ah) = _actionsWindow.MeasurePx(mon.Scale);
        int ax = sel.Right - aw, ay = sel.Bottom + gap;
        if (ay + ah > area.Bottom - m)
        {
            ay = sel.Y - ah - gap;
            if (ay < area.Y + m) ay = sel.Bottom - ah - gap;
        }
        ax = Math.Clamp(ax, area.X + m, Math.Max(area.X + m, area.Right - aw - m));
        ay = Math.Clamp(ay, area.Y + m, Math.Max(area.Y + m, area.Bottom - ah - m));
        _actionsWindow.ShowAt(new PxRect(ax, ay, aw, ah));

        if (!tools)
        {
            _toolsWindow.HideBar();
            _styleWindow.HideBar();
            _styles.CommitPicker();
            return;
        }

        // tools: vertical strip right of the selection (left / inside when there is no room)
        double fit = Math.Min(1.0, (area.Height - 2.0 * m) / Math.Max(1, _toolsWindow.MeasurePx(mon.Scale, 1.0).H));
        var (tw, th) = _toolsWindow.MeasurePx(mon.Scale, fit);
        int tx = sel.Right + gap;
        if (tx + tw > area.Right - m)
        {
            tx = sel.X - gap - tw;
            if (tx < area.X + m) tx = sel.Right - tw - gap;
        }
        int ty = Math.Clamp(sel.Y, area.Y + m, Math.Max(area.Y + m, area.Bottom - th - m));
        tx = Math.Clamp(tx, area.X + m, Math.Max(area.X + m, area.Right - tw - m));
        _toolsWindow.ShowAt(new PxRect(tx, ty, tw, th));

        // style: bottom-left of the selection, beside the actions (own row when they would overlap)
        bool styleOn = Surface.Tool != EditorTool.None || Surface.Selected != null || Surface.IsEditingText;
        if (!styleOn)
        {
            _styleWindow.HideBar();
            _styles.CommitPicker();
            return;
        }
        var (sw, sh) = _styleWindow.MeasurePx(mon.Scale);
        int sx = sel.X, sy = ay;
        if (sx + sw + gap > ax)
        {
            // not enough room left of the actions: a row below (or above) them
            sy = ay >= sel.Bottom ? ay + ah + gap : ay - sh - gap;
            if (sy + sh > area.Bottom - m || sy < area.Y + m) sy = ay >= sel.Bottom ? ay - sh - gap : ay + ah + gap;
            sx = sel.Right - sw;
        }
        sx = Math.Clamp(sx, area.X + m, Math.Max(area.X + m, area.Right - sw - m));
        sy = Math.Clamp(sy, area.Y + m, Math.Max(area.Y + m, area.Bottom - sh - m));
        _styleWindow.ShowAt(new PxRect(sx, sy, sw, sh));
    }

    // ================================================================ input from the session

    /// <summary>The overlay was pressed: finish text / color picking first.</summary>
    public void OnOverlayMouseDown()
    {
        _layoutKey = null; // the overlay came to the front: re-raise our windows afterwards
        _styles.CommitPicker();
        Surface.CommitText();
    }

    /// <summary>A letter picks a tool; the same letter again (or the active tool button) = no tool.</summary>
    public void ToggleTool(EditorTool tool)
    {
        if (_session.IsCrossDpi) return;
        Surface.SetTool(Surface.Tool == tool ? EditorTool.None : tool);
    }

    /// <summary>Esc inside the editor (drawing in progress, selection, tool). False = nothing left to cancel.</summary>
    public bool Escape()
    {
        if (_styles.IsPickerOpen)
        {
            _styles.CancelPicker();
            return true;
        }
        if (Surface.CancelStep()) return true;
        if (Surface.Tool != EditorTool.None)
        {
            Surface.SetTool(EditorTool.None);
            return true;
        }
        return false;
    }

    /// <summary>Window to own dialogs (above the overlays and the edit window).</summary>
    public Window DialogOwner => HostVisible ? _host : _actionsWindow;

    private void OnTextEditing(bool on)
    {
        UpdateTextInput();
        if (on)
        {
            // typing (Thai input, IME, language switch) needs a real focused, foreground window
            try
            {
                NativeMethods.ForceForeground(new WindowInteropHelper(_host).Handle);
                _host.Activate();
            }
            catch
            {
                // focus is best effort
            }
        }
    }

    // ================================================================ output

    /// <summary>The drawings rendered over the screenshot for a screen rectangle (origin = its screen position).</summary>
    public PixelImage Render(PxRect screenRect)
    {
        var shot = _session.Shot;
        var r = screenRect.Intersect(shot.Bounds);
        var bmp = Surface.RenderOutput(new Int32Rect(r.X - shot.OriginX, r.Y - shot.OriginY, r.Width, r.Height));
        var img = PixelImage.FromBitmapSource(bmp);
        return new PixelImage(img.Width, img.Height, img.Pixels, r.X, r.Y);
    }

    /// <summary>Take the drawings away (to continue in the editor window).</summary>
    public List<Annotation> DetachItems() => Surface.DetachItems();

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        if (_current == this) _current = null;
        if (_textInput) TextInputChanged?.Invoke(false);
        _styles.CommitPicker();
        Surface.CommitText();
        Surface.FlushPendingStyle();
        SaveStyle();
        foreach (var w in new Window[] { _toolsWindow, _styleWindow, _actionsWindow, _host })
        {
            try { w.Close(); } catch { /* ignore */ }
        }
    }

    /// <summary>Same settings as the editor window: the last style is used next time.</summary>
    private void SaveStyle()
    {
        try
        {
            var es = _settings.Current.Editor;
            var st = Surface.CurrentStyle;
            es.StrokeColor = ColorUtil.ToHex8(st.Stroke);
            es.FillColor = ColorUtil.ToHex8(st.Fill);
            es.FillEnabled = st.FillOn;
            es.Thickness = st.Thickness;
            es.FontSize = st.FontSize;
            _settings.Save();
        }
        catch (Exception ex)
        {
            Logger.Error("Saving the drawing style failed", ex);
        }
    }

    // ================================================================ windows

    /// <summary>Opaque window over the selection that hosts the edit surface.</summary>
    private sealed class EditHostWindow : Window
    {
        private readonly CaptureEditor _owner;
        private readonly EditSurface _surface;
        private readonly MatrixTransform _transform = new();
        private readonly Rectangle[] _handles = new Rectangle[8];
        private readonly Canvas _handleLayer = new() { IsHitTestVisible = false };
        private PxRect _placed;
        private double _scale = 1;
        private bool _cloaked;

        public Grid InputArea { get; }

        public EditHostWindow(CaptureEditor owner, EditSurface surface)
        {
            _owner = owner;
            _surface = surface;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Background = Brushes.Black;
            Title = "Freezia drawing";
            UseLayoutRounding = false;
            SnapsToDevicePixels = true;
            Left = -32000;
            Top = -32000;
            Width = 10;
            Height = 10;
            SetResourceReference(FontFamilyProperty, "UiFont");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var canvas = new Canvas { ClipToBounds = true };
            surface.RenderTransform = _transform;
            surface.HorizontalAlignment = HorizontalAlignment.Left;
            surface.VerticalAlignment = VerticalAlignment.Top;
            canvas.Children.Add(surface);
            InputArea = new Grid { Background = Brushes.Transparent };
            InputArea.Children.Add(canvas);
            InputArea.Children.Add(_handleLayer);
            Content = InputArea;
            for (int i = 0; i < 8; i++)
            {
                _handles[i] = new Rectangle { Fill = Brushes.White, Stroke = (Brush)FindResource("AccentBrush") };
                _handleLayer.Children.Add(_handles[i]);
            }

            InputArea.MouseLeftButtonDown += (_, e) =>
            {
                _owner._styles.CommitPicker();
                _surface.PointerDown(e.GetPosition(_surface.ExportRoot), e.ClickCount);
                e.Handled = true;
            };
            InputArea.MouseMove += (_, e) => _surface.PointerMove(e.GetPosition(_surface.ExportRoot));
            InputArea.MouseLeftButtonUp += (_, e) =>
            {
                _surface.PointerUp(e.GetPosition(_surface.ExportRoot));
                e.Handled = true;
            };
            InputArea.LostMouseCapture += (_, _) => _surface.PointerCaptureLost();
            InputArea.MouseLeave += (_, _) => _surface.PointerLeave();
            MouseRightButtonUp += (_, e) => e.Handled = true;
            MouseWheel += (_, e) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _surface.AdjustSize(e.Delta > 0 ? 1 : -1);
                e.Handled = true;
            };
            PreviewKeyDown += OnPreviewKeyDown;

            SourceInitialized += (_, _) =>
            {
                WindowHelper.MakeToolWindow(this);
                var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                src?.AddHook(WndProc);
                SetCloak(true);
            };
            Show();
        }

        /// <summary>Typing: Esc / Ctrl+Enter finish the text (all other keys go to the text box).</summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (_surface.ActiveTextBox is TextBox tb && tb.IsKeyboardFocusWithin)
            {
                if (key == Key.Escape || (key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
                {
                    _surface.CommitText();
                    e.Handled = true;
                }
            }
        }

        public void Place(PxRect sel, PixelImage shot)
        {
            var mon = Monitors.FromPoint(sel.X + sel.Width / 2, sel.Y + sel.Height / 2);
            double s = mon.Scale;
            if (sel != _placed || Math.Abs(s - _scale) > 0.001)
            {
                _placed = sel;
                _scale = s;
                WindowHelper.SetPhysicalBounds(this, sel);
                Width = sel.Width / s;
                Height = sel.Height / s;
                // image pixel (shot coordinates) -> window DIP, 1 image px = 1 device px
                double ox = sel.X - shot.OriginX, oy = sel.Y - shot.OriginY;
                _transform.Matrix = new Matrix(1 / s, 0, 0, 1 / s, -ox / s, -oy / s);
                _surface.ViewScale = 1 / s;
                PlaceHandles(sel.Width / s, sel.Height / s);
            }
            else
            {
                WindowHelper.SetPhysicalBounds(this, sel); // keep it above the overlays (z-order)
            }
        }

        private void PlaceHandles(double w, double h)
        {
            // the inner halves of the frame handles, drawn again on top of the drawings
            double hs = 7, half = hs / 2, onePx = 1 / _scale;
            double[] xs = { 0, w / 2, w };
            double[] ys = { 0, h / 2, h };
            (int, int)[] idx = { (0, 0), (1, 0), (2, 0), (2, 1), (2, 2), (1, 2), (0, 2), (0, 1) };
            for (int i = 0; i < 8; i++)
            {
                var r = _handles[i];
                r.Width = r.Height = hs;
                r.StrokeThickness = onePx;
                Canvas.SetLeft(r, xs[idx[i].Item1] - half);
                Canvas.SetTop(r, ys[idx[i].Item2] - half);
            }
        }

        public void Cloak()
        {
            SetCloak(true);
        }

        /// <summary>Show again once WPF has rendered the new position (no stale frame).</summary>
        public void Uncloak()
        {
            if (!_cloaked) return;
            int frames = 0;
            void OnRender(object? s, EventArgs e)
            {
                if (++frames < 2) return;
                CompositionTarget.Rendering -= OnRender;
                if (_owner.HostVisible) SetCloak(false);
            }
            CompositionTarget.Rendering += OnRender;
        }

        private void SetCloak(bool on)
        {
            _cloaked = on;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int v = on ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref v, sizeof(int));
        }

        private const int DWMWA_CLOAK = 13;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCHITTEST = 0x0084, WM_MOUSEACTIVATE = 0x0021, HTTRANSPARENT = -1, MA_NOACTIVATE = 3;
            if (msg == WM_NCHITTEST)
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF), y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                // no tool, or on the frame handles: the overlay underneath moves / resizes the frame
                if (_surface.Tool == EditorTool.None || _owner._session.IsOnHandle(x, y))
                {
                    handled = true;
                    return (IntPtr)HTTRANSPARENT;
                }
            }
            else if (msg == WM_MOUSEACTIVATE && !_surface.IsEditingText)
            {
                // clicks draw without stealing activation (keys are routed by the hook anyway)
                handled = true;
                return (IntPtr)MA_NOACTIVATE;
            }
            return IntPtr.Zero;
        }
    }

    /// <summary>Small borderless card (tool strip, style row, action bar) placed in physical pixels.</summary>
    private sealed class ToolbarWindow : Window
    {
        private readonly Border _card;
        private readonly FrameworkElement _content;
        private bool _shown;

        public ToolbarWindow(FrameworkElement content, bool activatable)
        {
            _content = content;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Title = "Freezia tools";
            SizeToContent = SizeToContent.Manual;
            UseLayoutRounding = true;
            SetResourceReference(FontFamilyProperty, "UiFont");
            SetResourceReference(ForegroundProperty, "TextBrush");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            _card = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Margin = new Thickness(6),
                Child = content,
                Cursor = Cursors.Arrow,
            };
            _card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            _card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            _card.Effect = (System.Windows.Media.Effects.Effect)FindResource("SoftShadow");
            Content = _card;
            Left = -32000;
            Top = -32000;
            SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this, noActivate: !activatable);
        }

        /// <summary>Size in physical pixels (card + shadow margin), optionally scaled to fit.</summary>
        public (int W, int H) MeasurePx(double scale, double fit = 1.0)
        {
            _content.LayoutTransform = fit < 0.999 ? new ScaleTransform(fit, fit) : Transform.Identity;
            _card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var d = _card.DesiredSize;
            return ((int)Math.Ceiling(d.Width * scale), (int)Math.Ceiling(d.Height * scale));
        }

        public void ShowAt(PxRect r)
        {
            if (!_shown)
            {
                Show();
                _shown = true;
            }
            else if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
            WindowHelper.SetPhysicalBounds(this, r);
            double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Width = r.Width / s;
            Height = r.Height / s;
        }

        public void HideBar()
        {
            if (_shown && Visibility == Visibility.Visible) Visibility = Visibility.Hidden;
        }
    }
}
