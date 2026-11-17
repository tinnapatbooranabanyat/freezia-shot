using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Freezia.Capture;
using Freezia.Core;
using Freezia.UI;

namespace Freezia.Editor;

/// <summary>
/// Editor window: hosts an <see cref="EditSurface"/> with a <see cref="ToolStrip"/> and a <see cref="StyleBar"/>,
/// and adds what only a window has: zoom / scroll, crop and resize, opening files, and the output buttons.
/// </summary>
public partial class EditorWindow : Window
{
    private PixelImage _image;
    private readonly CaptureController _controller;
    private readonly SettingsService _settings;

    /// <summary>The editing core (image, drawings, tools, undo).</summary>
    public EditSurface Surface { get; }

    // crop
    private readonly Path _cropShade;
    private readonly Rectangle _cropFrame;
    private Rect? _cropRect;
    private bool _cropping;
    private Point _cropStart;

    // zoom: 1.0 = one image pixel per screen pixel
    private double _zoom = 1.0;
    private double _dpiScale = 1.0;
    private bool _dirty;

    public EditorWindow(PixelImage image, CaptureController controller, SettingsService settings)
    {
        InitializeComponent();
        _image = image;
        _controller = controller;
        _settings = settings;

        var es = settings.Current.Editor;
        var style = new AnnotationStyle(
            ColorUtil.Parse(es.StrokeColor, StyleBar.Presets[0]),
            ColorUtil.Parse(es.FillColor, Color.FromArgb(0x66, 0xFF, 0xD6, 0x0A)),
            es.FillEnabled, es.Thickness, es.FontSize);
        Surface = new EditSurface(image, style) { InputElement = Pad };
        ZoomHost.Children.Add(Surface);
        ImageInfo.Text = $"{image.Width} × {image.Height} px";

        _cropShade = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), Visibility = Visibility.Collapsed };
        _cropFrame = new Rectangle { Stroke = Brushes.White, StrokeDashArray = new DoubleCollection { 4, 3 }, Visibility = Visibility.Collapsed };
        Surface.AdornerLayer.Children.Insert(0, _cropShade);
        Surface.AdornerLayer.Children.Add(_cropFrame);
        Pad.MouseLeave += (_, _) => Surface.PointerLeave();

        Tools.Attach(Surface);
        Tools.ResizeRequested += OnResize;
        Styles.Attach(Surface, settings);
        Surface.StateChanged += OnSurfaceState;
        Surface.CommandsChanged += UpdateCommandStates;
        Surface.PointerMoved += ShowCursorInfo;

        ApplyLanguage();
        Loc.Changed += ApplyLanguage;
        Closed += (_, _) => Loc.Changed -= ApplyLanguage;

        Surface.Undo.Changed += (_, _) =>
        {
            _dirty = true;
            UpdateCommandStates();
        };

        Pad.MouseLeftButtonDown += OnCanvasDown;
        Pad.MouseMove += OnCanvasMove;
        Pad.MouseLeftButtonUp += OnCanvasUp;
        Pad.LostMouseCapture += (_, _) =>
        {
            if (_cropping) _cropping = false;
            Surface.PointerCaptureLost();
        };
        Scroller.PreviewMouseWheel += OnWheel;
        PreviewKeyDown += OnPreviewKeyDown;
        DpiChanged += (_, e) =>
        {
            _dpiScale = e.NewDpi.DpiScaleX;
            ApplyZoom(_zoom, null);
        };
        Closing += OnClosing;

        WindowHelper.ApplyChrome(this);
        SourceInitialized += (_, _) =>
            System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        SizeToImage();
        Loaded += (_, _) =>
        {
            _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Dispatcher.BeginInvoke(InitialZoom, DispatcherPriority.Loaded);
        };

        Surface.SetTool(EditorTool.Arrow);
        UpdateCommandStates();
        _dirty = false;
    }

    private string? _sourcePath;

    /// <summary>File the image was opened from (null for screen captures).</summary>
    public string? SourcePath
    {
        get => _sourcePath;
        set { _sourcePath = value; UpdateTitle(); }
    }

    private void UpdateTitle()
    {
        string size = $"({_image.Width} × {_image.Height})";
        Title = _sourcePath != null
            ? $"{System.IO.Path.GetFileName(_sourcePath)}  {size} — {Strings.EditorTitle}"
            : $"{Strings.EditorTitle}  {size}";
    }

    /// <summary>Image files dropped on the editor open in their own editor windows.</summary>
    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop) &&
                  e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(Core.ImageLoader.IsSupported);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            App.Current.OpenImageFiles(files.Where(Core.ImageLoader.IsSupported));
        e.Handled = true;
    }

    /// <summary>All UI text (re-run when the language changes while the window is open).</summary>
    private void ApplyLanguage()
    {
        UpdateTitle();
        Tools.ApplyLanguage();
        CropHint.Text = Strings.CropHint;
        CropApplyButton.Content = Strings.CropApply;
        ClearAllButton.ToolTip = Strings.ClearAll;
        PinButton.ToolTip = Strings.ActionPin;
        OcrButton.ToolTip = Strings.ActionOcr;
        QrButton.ToolTip = Strings.ActionQr;
        UndoButton.ToolTip = Strings.Undo;
        RedoButton.ToolTip = Strings.Redo;
        DeleteButton.ToolTip = Strings.Delete;
        CopyText.Text = Strings.ActionCopy;
        SaveAsText.Text = Strings.ActionSaveAs;
        SaveText.Text = Strings.ActionSave;
        CopyButton.ToolTip = Strings.Copy;
        SaveAsButton.ToolTip = Strings.SaveAs;
        SaveButton.ToolTip = Strings.Save;
        ZoomInButton.ToolTip = Strings.ZoomIn;
        ZoomOutButton.ToolTip = Strings.ZoomOut;
        FitButton.ToolTip = Strings.ZoomFit;
        ActualButton.ToolTip = Strings.Zoom100;
        Styles.ApplyLanguage();
    }

    // ================================================================ window

    private void SizeToImage()
    {
        var mon = Monitors.FromCursor();
        double s = mon.Scale;
        double waW = mon.WorkArea.Width / s, waH = mon.WorkArea.Height / s;
        double w = _image.Width / s + 56 + 30;
        double h = _image.Height / s + 56 + 215; // title + 2 toolbars + status bar
        Width = Math.Max(MinWidth, Math.Min(w, waW * 0.92));
        Height = Math.Max(MinHeight, Math.Min(h, waH * 0.92));
        if (Width > waW) Width = waW;
        if (Height > waH) Height = waH;
        WindowHelper.CenterOnCursorMonitor(this);
    }

    private void InitialZoom()
    {
        double fit = FitZoom();
        ApplyZoom(Math.Min(1.0, fit), null);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        Surface.CommitText();
        Surface.FlushPendingStyle();
        Styles.CommitPicker();
        if (_dirty && Surface.Items.Count > 0)
        {
            if (!ConfirmDialog.Show(this, Strings.UnsavedClose, Strings.UnsavedCloseYes, Strings.UnsavedCloseNo))
            {
                e.Cancel = true;
                return;
            }
        }
        SaveEditorSettings();
    }

    private void SaveEditorSettings()
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

    // ================================================================ tools

    private void OnSurfaceState()
    {
        bool crop = Surface.Tool == EditorTool.Crop;
        if (!crop && (_cropRect != null || _cropping)) ClearCrop();
        CropHint.Visibility = CropApplyButton.Visibility = crop ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetTool(EditorTool tool) => Surface.SetTool(tool);

    private Point ImagePoint(MouseEventArgs e) => e.GetPosition(Surface.ExportRoot);

    private bool InsideImage(Point p) => p.X >= 0 && p.Y >= 0 && p.X < _image.Width && p.Y < _image.Height;

    private void ShowCursorInfo(Point p)
    {
        if (!InsideImage(p)) CursorInfo.Text = "";
        else if (Styles.IsEyedropperActive)
            CursorInfo.Text = $"X: {(int)p.X}  Y: {(int)p.Y}   {ColorUtil.ToHex6(_image.GetPixel((int)p.X, (int)p.Y))}";
        else CursorInfo.Text = $"X: {(int)p.X}  Y: {(int)p.Y}";
    }

    // ================================================================ mouse

    private void OnCanvasDown(object sender, MouseButtonEventArgs e)
    {
        var p = ImagePoint(e);
        if (Surface.Tool == EditorTool.Crop && !Styles.IsPickerOpen)
        {
            Focus();
            Surface.CommitText();
            _cropping = true;
            _cropStart = p;
            _cropRect = new Rect(ClampToImage(p), ClampToImage(p));
            UpdateCropOverlay();
            Pad.CaptureMouse();
            e.Handled = true;
            return;
        }
        Surface.PointerDown(p, e.ClickCount);
        e.Handled = true;
    }

    private void OnCanvasMove(object sender, MouseEventArgs e)
    {
        var p = ImagePoint(e);
        if (_cropping)
        {
            ShowCursorInfo(p);
            _cropRect = new Rect(ClampToImage(_cropStart), ClampToImage(p));
            UpdateCropOverlay();
            return;
        }
        Surface.PointerMove(p);
    }

    private void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (_cropping)
        {
            _cropping = false;
            if (Pad.IsMouseCaptured) Pad.ReleaseMouseCapture();
            if (_cropRect is Rect cr && (cr.Width < 2 || cr.Height < 2)) ClearCrop();
            return;
        }
        Surface.PointerUp(ImagePoint(e));
    }

    private void OnClearAll(object sender, RoutedEventArgs e) => Surface.ClearAll();

    // ================================================================ crop + resize

    private Point ClampToImage(Point p) => new(Math.Clamp(Math.Round(p.X), 0, _image.Width), Math.Clamp(Math.Round(p.Y), 0, _image.Height));

    private void UpdateCropOverlay()
    {
        if (_cropRect is not Rect r)
        {
            _cropShade.Visibility = _cropFrame.Visibility = Visibility.Collapsed;
            return;
        }
        double scale = ZoomTransform.ScaleX > 0 ? ZoomTransform.ScaleX : 1;
        var shade = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, _image.Width, _image.Height)), new RectangleGeometry(r));
        _cropShade.Data = shade;
        _cropShade.Visibility = Visibility.Visible;
        _cropFrame.StrokeThickness = 1.5 / scale;
        Canvas.SetLeft(_cropFrame, r.X);
        Canvas.SetTop(_cropFrame, r.Y);
        _cropFrame.Width = r.Width;
        _cropFrame.Height = r.Height;
        _cropFrame.Visibility = Visibility.Visible;
        CropHint.Text = $"{(int)r.Width} × {(int)r.Height}  •  {Strings.CropHint}";
    }

    private void ClearCrop()
    {
        _cropRect = null;
        if (_cropping)
        {
            _cropping = false;
            if (Pad.IsMouseCaptured) Pad.ReleaseMouseCapture();
        }
        UpdateCropOverlay();
        CropHint.Text = Strings.CropHint;
    }

    private void OnCropApply(object sender, RoutedEventArgs e) => ApplyCrop();

    /// <summary>Crop the screenshot; drawings stay editable and keep their place on the image.</summary>
    private void ApplyCrop()
    {
        if (_cropRect is not Rect r) return;
        Surface.CommitText();
        Surface.FlushPendingStyle();
        var pr = new PxRect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height).Intersect(new PxRect(0, 0, _image.Width, _image.Height));
        if (pr.Width < 2 || pr.Height < 2) return;
        var oldImage = _image;
        var newImage = oldImage.Crop(pr);
        var shift = new Vector(-pr.X, -pr.Y);
        ClearCrop();
        Surface.Undo.Do("crop", () => ReplaceImage(oldImage, -shift), () => ReplaceImage(newImage, shift));
    }

    private void ReplaceImage(PixelImage img, Vector shift)
    {
        _image = img;
        Surface.ReplaceImage(img, shift);
        ImageInfo.Text = $"{img.Width} × {img.Height} px";
        UpdateTitle();
        Dispatcher.BeginInvoke(InitialZoom, DispatcherPriority.Loaded);
    }

    private void OnResize()
    {
        Surface.CommitText();
        Surface.FlushPendingStyle();
        var dlg = new ResizeDialog(this, _image.Width, _image.Height);
        if (dlg.ShowDialog() != true) return;
        ResizeTo(dlg.ResultWidth, dlg.ResultHeight);
    }

    /// <summary>Scale the image. Drawings are merged into it first (one undo step restores everything).</summary>
    private void ResizeTo(int w, int h)
    {
        if (w == _image.Width && h == _image.Height) return;
        var flattened = RenderOutput();
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen()) dc.DrawImage(flattened, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        var newImage = PixelImage.FromBitmapSource(rtb);
        var oldImage = _image;
        var oldItems = Surface.Items.ToList();
        Surface.Undo.Do("resize",
            () =>
            {
                ReplaceImage(oldImage, new Vector());
                for (int i = 0; i < oldItems.Count; i++) Surface.AddItem(oldItems[i], i);
            },
            () =>
            {
                foreach (var a in oldItems) Surface.RemoveItem(a);
                ReplaceImage(newImage, new Vector());
            });
    }

    /// <summary>Take over drawings made on the capture overlay (coordinates moved by <paramref name="shift"/>).</summary>
    public void ImportItems(IEnumerable<Annotation> items, Vector shift)
    {
        Surface.AttachItems(items, shift);
        _dirty = Surface.Items.Count > 0;
    }

    private void UpdateCommandStates()
    {
        UndoButton.IsEnabled = Surface.Undo.CanUndo;
        RedoButton.IsEnabled = Surface.Undo.CanRedo;
        DeleteButton.IsEnabled = Surface.Selected != null;
        ClearAllButton.IsEnabled = Surface.Items.Count > 0;
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Surface.DoUndo();
    private void OnRedo(object sender, RoutedEventArgs e) => Surface.DoRedo();
    private void OnDelete(object sender, RoutedEventArgs e) => Surface.DeleteSelected();

    // ================================================================ keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);

        // Typing text: only Esc / Ctrl+Enter finish it.
        if (Surface.ActiveTextBox is TextBox tb && tb.IsKeyboardFocusWithin)
        {
            if (key == Key.Escape || (key == Key.Enter && ctrl))
            {
                Surface.CommitText();
                e.Handled = true;
            }
            return;
        }
        // The color picker handles its own keys.
        if (Styles.PickerHasKeyboardFocus) return;
        if (Keyboard.FocusedElement is TextBox) return;

        if (ctrl)
        {
            switch (key)
            {
                case Key.Z when shift: Surface.DoRedo(); break;
                case Key.Z: Surface.DoUndo(); break;
                case Key.Y: Surface.DoRedo(); break;
                case Key.C: Copy(); break;
                case Key.Delete when shift: Surface.ClearAll(); break;
                case Key.S when shift: SaveAs(); break;
                case Key.O: App.Current.OpenImageDialog(this); break;
                case Key.S: Save(); break;
                case Key.D0 or Key.NumPad0: ApplyZoom(FitZoom(), null); break;
                case Key.D1 or Key.NumPad1: ApplyZoom(1.0, null); break;
                case Key.OemPlus or Key.Add: ZoomStep(+1, null); break;
                case Key.OemMinus or Key.Subtract: ZoomStep(-1, null); break;
                default: return;
            }
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.Delete:
            case Key.Back:
                Surface.DeleteSelected();
                break;
            case Key.Escape:
                if (Styles.IsPickerOpen) Styles.CancelPicker();
                else if (_cropRect != null || _cropping) ClearCrop();
                else Surface.CancelStep();
                break;
            case Key.C: SetTool(EditorTool.Crop); break;
            case Key.Enter when Surface.Tool == EditorTool.Crop && _cropRect != null: ApplyCrop(); break;
            case Key.Left or Key.Right or Key.Up or Key.Down when Surface.Selected != null:
                Surface.NudgeSelected(key, shift ? 10 : 1);
                break;
            default:
                if (EditSurface.ToolForKey(key) is EditorTool t) SetTool(t);
                else return;
                break;
        }
        e.Handled = true;
    }

    // ================================================================ zoom

    private double FitZoom()
    {
        double vw = Math.Max(50, Scroller.ActualWidth - 2 * 28 - 4);
        double vh = Math.Max(50, Scroller.ActualHeight - 2 * 28 - 4);
        double scale = Math.Min(vw / _image.Width, vh / _image.Height); // DIP per pixel
        return scale * _dpiScale;
    }

    private static readonly double[] ZoomSteps = { 0.05, 0.1, 0.15, 0.2, 0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1, 1.25, 1.5, 2, 3, 4, 6, 8, 12, 16 };

    private void ZoomStep(int dir, Point? anchor)
    {
        double next = dir > 0
            ? ZoomSteps.FirstOrDefault(z => z > _zoom + 0.001, ZoomSteps[^1])
            : ZoomSteps.LastOrDefault(z => z < _zoom - 0.001, ZoomSteps[0]);
        ApplyZoom(next, anchor);
    }

    /// <param name="anchor">Point in Scroller coordinates that should stay fixed (null = center).</param>
    private void ApplyZoom(double zoom, Point? anchor)
    {
        zoom = Math.Clamp(zoom, 0.02, 16);
        var a = anchor ?? new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2);
        Point imgPt;
        try { imgPt = Scroller.TranslatePoint(a, Surface.ExportRoot); }
        catch { imgPt = new Point(_image.Width / 2.0, _image.Height / 2.0); }

        _zoom = zoom;
        double s = zoom / _dpiScale;
        ZoomTransform.ScaleX = ZoomTransform.ScaleY = s;
        RenderOptions.SetBitmapScalingMode(Surface.BaseImage, zoom >= 0.999 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        ZoomText.Text = $"{Math.Round(zoom * 100)}%";

        Scroller.UpdateLayout();
        try
        {
            var now = Surface.ExportRoot.TranslatePoint(imgPt, Scroller);
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + now.X - a.X);
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + now.Y - a.Y);
        }
        catch
        {
            // not in the visual tree yet
        }
        Surface.ViewScale = s;
        UpdateCropOverlay();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control))
        {
            ZoomStep(e.Delta > 0 ? +1 : -1, e.GetPosition(Scroller));
            e.Handled = true;
        }
        else if (mods.HasFlag(ModifierKeys.Shift))
        {
            ScrollHorizontally(-e.Delta);
            e.Handled = true;
        }
    }

    private void ScrollHorizontally(double delta) =>
        Scroller.ScrollToHorizontalOffset(Math.Max(0, Scroller.HorizontalOffset + delta));

    /// <summary>WM_MOUSEHWHEEL (tilt wheel, touchpad sideways scroll) is not a WPF event.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEHWHEEL = 0x020E;
        if (msg == WM_MOUSEHWHEEL && Scroller.IsMouseOver)
        {
            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            ScrollHorizontally(delta);
            handled = true;
            return (IntPtr)1;
        }
        return IntPtr.Zero;
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => ZoomStep(+1, null);
    private void OnZoomOut(object sender, RoutedEventArgs e) => ZoomStep(-1, null);
    private void OnZoomFit(object sender, RoutedEventArgs e) => ApplyZoom(FitZoom(), null);
    private void OnZoom100(object sender, RoutedEventArgs e) => ApplyZoom(1.0, null);

    // ================================================================ export

    /// <summary>Render image + annotations at the exact captured pixel size (96 DPI, 1 DIP = 1 px).</summary>
    public BitmapSource RenderOutput() => Surface.RenderOutput();

    private void OnCopy(object sender, RoutedEventArgs e) => Copy();

    /// <summary>The edited image (drawings included) as pixels, for pin / OCR / QR.</summary>
    private PixelImage EditedImage() => PixelImage.FromBitmapSource(RenderOutput());

    private void OnPin(object sender, RoutedEventArgs e) => _controller.Pin(EditedImage(), atCapturePosition: false);
    private void OnOcr(object sender, RoutedEventArgs e) => _controller.Ocr(EditedImage());
    private void OnQr(object sender, RoutedEventArgs e) => _controller.ReadCodes(EditedImage());
    private void OnSave(object sender, RoutedEventArgs e) => Save();
    private void OnSaveAs(object sender, RoutedEventArgs e) => SaveAs();

    private void Copy()
    {
        var img = RenderOutput();
        _controller.Remember(img);
        if (_controller.Copy(img)) _dirty = false;
    }

    private void Save()
    {
        var img = RenderOutput();
        _controller.Remember(img);
        if (_controller.Save(img) != null) _dirty = false;
    }

    private void SaveAs()
    {
        var img = RenderOutput();
        _controller.Remember(img);
        if (_controller.SaveAs(img, this, SourcePath) != null) _dirty = false;
    }
}
