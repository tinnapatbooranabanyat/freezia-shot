using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Freezia.Capture;
using Freezia.Core;
using Freezia.UI;

namespace Freezia.Editor;

public partial class EditorWindow : Window
{
    private enum ColorTarget { Stroke, Fill }

    private static readonly Color[] Presets =
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

    private readonly PixelImage _image;
    private readonly CaptureController _controller;
    private readonly SettingsService _settings;
    private readonly UndoManager _undo = new();
    private readonly List<Annotation> _items = new();
    private readonly Dictionary<EditorTool, RadioButton> _toolButtons;

    // current style
    private Color _stroke, _fill;
    private bool _fillOn;
    private double _thickness, _fontSize;
    private ColorTarget _target = ColorTarget.Stroke;

    private EditorTool _tool = EditorTool.Brush;
    private Annotation? _selected;
    private readonly Rectangle _selectionOutline;

    // drag state
    private Annotation? _drawing;
    private Point _dragStart;
    private bool _moving;
    private Vector _moveStartOffset;

    // text editing
    private TextBox? _textBox;
    private TextAnnotation? _editingText;
    private Point _textOrigin;

    // color picker session
    private ColorTarget _pickerTarget;
    private Color _pickerOriginalColor;
    private bool _pickerOriginalFillOn;
    private Annotation? _pickerSelected;
    private AnnotationStyle? _pickerSelectedOriginal;
    private DateTime _pickerClosedAt;

    // slider edits on a selected object are merged into one undo step
    private Annotation? _pendingStyleTarget;
    private AnnotationStyle? _pendingStyleOriginal;
    private readonly DispatcherTimer _pendingStyleTimer;
    private bool _updatingUi;

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
        _stroke = ColorUtil.Parse(es.StrokeColor, Presets[0]);
        _fill = ColorUtil.Parse(es.FillColor, Color.FromArgb(0x66, 0xFF, 0xD6, 0x0A));
        _fillOn = es.FillEnabled;
        _thickness = es.Thickness;
        _fontSize = es.FontSize;

        BaseImage.Source = image.Source;
        ExportRoot.Width = image.Width;
        ExportRoot.Height = image.Height;
        ImageInfo.Text = $"{image.Width} × {image.Height} px";

        _selectionOutline = new Rectangle
        {
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Visibility = Visibility.Collapsed,
        };
        AdornerLayer.Children.Add(_selectionOutline);

        _toolButtons = new()
        {
            [EditorTool.Select] = ToolSelect,
            [EditorTool.Brush] = ToolBrush,
            [EditorTool.Highlighter] = ToolHighlighter,
            [EditorTool.Line] = ToolLine,
            [EditorTool.Arrow] = ToolArrow,
            [EditorTool.Rect] = ToolRect,
            [EditorTool.Ellipse] = ToolEllipse,
            [EditorTool.Text] = ToolText,
            [EditorTool.Mosaic] = ToolMosaic,
            [EditorTool.Step] = ToolStep,
        };
        foreach (var (tool, rb) in _toolButtons)
        {
            var t = tool;
            rb.Checked += (_, _) => SetTool(t);
        }


        foreach (var c in Presets) PresetPanel.Children.Add(MakeSwatch(c, removable: false));
        BuildSaved();
        BuildRecent();
        ApplyLanguage();
        Loc.Changed += ApplyLanguage;
        Closed += (_, _) => Loc.Changed -= ApplyLanguage;

        SizeSlider.ValueChanged += OnSizeSliderChanged;
        _pendingStyleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _pendingStyleTimer.Tick += (_, _) => FlushPendingStyle();

        Picker.ColorChanged += c => ApplyPickerColor(c);
        Picker.Committed += OnPickerCommitted;
        Picker.Cancelled += OnPickerCancelled;
        Picker.EyedropperToggled += on => Pad.Cursor = on ? Cursors.Cross : null;
        Picker.SaveRequested += SaveColor;

        _undo.Changed += (_, _) =>
        {
            _dirty = true;
            UpdateCommandStates();
        };

        Pad.MouseLeftButtonDown += OnCanvasDown;
        Pad.MouseMove += OnCanvasMove;
        Pad.MouseLeftButtonUp += OnCanvasUp;
        Pad.LostMouseCapture += (_, _) => { if (_drawing != null || _moving) FinishDrag(); };
        Scroller.PreviewMouseWheel += OnWheel;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnWindowPreviewMouseDown;
        Deactivated += (_, _) => { if (PickerPopup.IsOpen && !Picker.IsEyedropperActive) Picker.Commit(); };
        LocationChanged += (_, _) => RepositionPopup();
        SizeChanged += (_, _) => RepositionPopup();
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

        _toolButtons[EditorTool.Arrow].IsChecked = true;
        UpdateStyleUi();
        UpdateCommandStates();
        _dirty = false;
    }

    /// <summary>All UI text (re-run when the language changes while the window is open).</summary>
    private void ApplyLanguage()
    {
        Title = $"{Strings.EditorTitle}  ({_image.Width} × {_image.Height})";
        ToolSelect.ToolTip = Strings.ToolSelect;
        ToolBrush.ToolTip = Strings.ToolBrush;
        ToolHighlighter.ToolTip = Strings.ToolHighlighter;
        ToolLine.ToolTip = Strings.ToolLine;
        ToolArrow.ToolTip = Strings.ToolArrow;
        ToolRect.ToolTip = Strings.ToolRect;
        ToolEllipse.ToolTip = Strings.ToolEllipse;
        ToolText.ToolTip = Strings.ToolText;
        ToolMosaic.ToolTip = Strings.ToolMosaic;
        ToolStep.ToolTip = Strings.ToolStep;
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
        StrokeTarget.ToolTip = Strings.StrokeColor;
        FillTarget.ToolTip = Strings.FillColor;
        FillToggle.Content = Strings.FillEnabled;
        PresetPanel.ToolTip = Strings.PresetColors;
        RecentPanel.ToolTip = Strings.RecentColors;
        StrokeCaption.Text = Loc.Language == AppLanguage.English ? "Stroke" : "เส้น";
        FillCaption.Text = Loc.Language == AppLanguage.English ? "Fill" : "เติม";
        SavedPanel.ToolTip = Strings.SavedColors;
        BuildSaved();
        if (PickerPopup.IsOpen) Picker.SetTitle(_pickerTarget == ColorTarget.Stroke ? Strings.StrokeColor : Strings.FillColor);
        UpdateStyleUi();
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
        CommitText();
        FlushPendingStyle();
        if (PickerPopup.IsOpen) Picker.Commit();
        if (_dirty && _items.Count > 0)
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
        es.StrokeColor = ColorUtil.ToHex8(_stroke);
        es.FillColor = ColorUtil.ToHex8(_fill);
        es.FillEnabled = _fillOn;
        es.Thickness = _thickness;
        es.FontSize = _fontSize;
        _settings.Save();
    }

    // ================================================================ tools

    private void SetTool(EditorTool tool)
    {
        if (_tool == tool && _toolButtons[tool].IsChecked == true) { UpdateStyleUi(); return; }
        CommitText();
        FlushPendingStyle();
        _tool = tool;
        if (_toolButtons[tool].IsChecked != true) _toolButtons[tool].IsChecked = true;
        if (tool != EditorTool.Select) Select(null);
        Pad.Cursor = tool switch
        {
            EditorTool.Select => Cursors.Arrow,
            EditorTool.Text => Cursors.IBeam,
            _ => Cursors.Cross,
        };
        UpdateStyleUi();
    }

    private AnnotationStyle CurrentStyle => new(_stroke, _fill, _fillOn, _thickness, _fontSize);

    private Point ImagePoint(MouseEventArgs e) => e.GetPosition(ExportRoot);

    private bool InsideImage(Point p) => p.X >= 0 && p.Y >= 0 && p.X < _image.Width && p.Y < _image.Height;

    // ================================================================ mouse

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!PickerPopup.IsOpen) return;
        // Mouse events inside the Popup bubble/tunnel through its logical parent (this window),
        // so ignore everything that happens inside the picker itself.
        if (Picker.IsMouseOver || IsInside(e.OriginalSource as DependencyObject, Picker)) return;
        // A click anywhere else in the window closes the picker, keeping the color.
        if (!Picker.IsEyedropperActive)
        {
            Picker.Commit();
            if (Pad.IsMouseOver) e.Handled = true; // the closing click does not draw
        }
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

    private void OnCanvasDown(object sender, MouseButtonEventArgs e)
    {
        var p = ImagePoint(e);
        Focus();

        if (PickerPopup.IsOpen && Picker.IsEyedropperActive)
        {
            if (InsideImage(p))
            {
                Picker.SetRgbKeepAlpha(_image.GetPixel((int)p.X, (int)p.Y));
                Picker.StopEyedropper();
            }
            e.Handled = true;
            return;
        }

        if (_textBox != null)
        {
            // clicking outside the text box finishes the text
            CommitText();
            e.Handled = true;
            if (_tool == EditorTool.Text) return;
        }

        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _dragStart = p;

        switch (_tool)
        {
            case EditorTool.Select:
            {
                var hit = HitTest(p);
                Select(hit);
                if (hit != null)
                {
                    if (e.ClickCount == 2 && hit is TextAnnotation ta)
                    {
                        BeginTextEdit(ta.Origin + ta.Offset, ta);
                        break;
                    }
                    _moving = true;
                    _moveStartOffset = hit.Offset;
                    Pad.CaptureMouse();
                }
                break;
            }
            case EditorTool.Brush:
            case EditorTool.Highlighter:
                StartDrawing(new StrokeAnnotation(CurrentStyle, p, _tool == EditorTool.Highlighter));
                break;
            case EditorTool.Line:
            case EditorTool.Arrow:
                StartDrawing(new LineAnnotation(CurrentStyle, p, _tool == EditorTool.Arrow));
                break;
            case EditorTool.Rect:
            case EditorTool.Ellipse:
                StartDrawing(new ShapeAnnotation(CurrentStyle, new Rect(p, p), _tool == EditorTool.Ellipse));
                break;
            case EditorTool.Mosaic:
                StartDrawing(new MosaicAnnotation(CurrentStyle, new Rect(p, p), _image));
                break;
            case EditorTool.Text:
            {
                if (HitTest(p) is TextAnnotation existing)
                    BeginTextEdit(existing.Origin + existing.Offset, existing);
                else if (InsideImage(p))
                    BeginTextEdit(p, null);
                break;
            }
            case EditorTool.Step:
            {
                if (!InsideImage(p)) break;
                int next = _items.OfType<StepAnnotation>().Select(s => s.Number).DefaultIfEmpty(0).Max() + 1;
                var step = new StepAnnotation(CurrentStyle, p, next);
                AddItem(step, _items.Count);
                PushAdd(step);
                break;
            }
        }
        e.Handled = true;
    }

    private void StartDrawing(Annotation a)
    {
        _drawing = a;
        AddItem(a, _items.Count);
        Pad.CaptureMouse();
    }

    private void OnCanvasMove(object sender, MouseEventArgs e)
    {
        var p = ImagePoint(e);
        if (!InsideImage(p)) CursorInfo.Text = "";
        else if (PickerPopup.IsOpen && Picker.IsEyedropperActive)
            CursorInfo.Text = $"X: {(int)p.X}  Y: {(int)p.Y}   {ColorUtil.ToHex6(_image.GetPixel((int)p.X, (int)p.Y))}";
        else CursorInfo.Text = $"X: {(int)p.X}  Y: {(int)p.Y}";

        if (_moving && _selected != null)
        {
            _selected.Offset = _moveStartOffset + (p - _dragStart);
            UpdateAdorner();
            return;
        }
        if (_drawing == null)
        {
            if (_tool == EditorTool.Select && !Picker.IsEyedropperActive)
                Pad.Cursor = HitTest(p) != null ? Cursors.SizeAll : Cursors.Arrow;
            return;
        }

        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (_drawing)
        {
            case StrokeAnnotation s:
                s.AddPoint(p);
                break;
            case LineAnnotation l:
                l.SetEnd(shift ? SnapAngle(_dragStart, p) : p);
                break;
            case ShapeAnnotation sh:
                sh.SetRect(DragRect(_dragStart, p, shift));
                break;
            case MosaicAnnotation m:
                m.SetRect(DragRect(_dragStart, p, shift));
                break;
        }
    }

    private void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (_drawing != null || _moving)
        {
            var p = ImagePoint(e);
            if (_drawing is StrokeAnnotation s) s.AddPoint(p);
            FinishDrag();
        }
    }

    private void FinishDrag()
    {
        if (_moving && _selected != null)
        {
            var a = _selected;
            var from = _moveStartOffset;
            var to = a.Offset;
            _moving = false;
            if ((to - from).LengthSquared > 0.01)
                _undo.Push("move", () => { a.Offset = from; UpdateAdorner(); }, () => { a.Offset = to; UpdateAdorner(); });
        }
        _moving = false;

        if (_drawing != null)
        {
            var a = _drawing;
            _drawing = null;
            bool tooSmall = a switch
            {
                LineAnnotation l => (l.End - l.Start).Length < 2,
                ShapeAnnotation sh => sh.Rect.Width < 2 && sh.Rect.Height < 2,
                MosaicAnnotation m => m.Rect.Width < 2 || m.Rect.Height < 2,
                _ => false,
            };
            if (tooSmall) RemoveItem(a);
            else PushAdd(a);
        }
        if (Pad.IsMouseCaptured) Pad.ReleaseMouseCapture();
    }

    private static Point SnapAngle(Point a, Point b)
    {
        Vector d = b - a;
        double len = d.Length;
        if (len < 0.5) return b;
        double ang = Math.Atan2(d.Y, d.X);
        double snapped = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
        return a + new Vector(Math.Cos(snapped) * len, Math.Sin(snapped) * len);
    }

    private static Rect DragRect(Point a, Point b, bool square)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        if (square)
        {
            double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            dx = Math.Sign(dx == 0 ? 1 : dx) * side;
            dy = Math.Sign(dy == 0 ? 1 : dy) * side;
        }
        return new Rect(a, new Point(a.X + dx, a.Y + dy));
    }

    private Annotation? HitTest(Point p)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var a = _items[i];
            if (a.Element.Visibility == Visibility.Visible && a.HitTest(p)) return a;
        }
        return null;
    }

    // ================================================================ items + undo

    private void AddItem(Annotation a, int index)
    {
        index = Math.Clamp(index, 0, _items.Count);
        _items.Insert(index, a);
        DrawCanvas.Children.Insert(index, a.Element);
    }

    private void RemoveItem(Annotation a)
    {
        if (_selected == a) Select(null);
        _items.Remove(a);
        DrawCanvas.Children.Remove(a.Element);
    }

    private void PushAdd(Annotation a)
    {
        int index = _items.IndexOf(a);
        _undo.Push("add", () => RemoveItem(a), () => AddItem(a, index));
    }

    private void DeleteSelected()
    {
        if (_selected == null) return;
        FlushPendingStyle();
        var a = _selected;
        int index = _items.IndexOf(a);
        _undo.Do("delete", () => AddItem(a, index), () => RemoveItem(a));
    }

    private void Select(Annotation? a)
    {
        FlushPendingStyle();
        _selected = a;
        if (a != null)
        {
            // toolbar reflects the selected object's style
            _stroke = a.Style.Stroke;
            _fill = a.Style.Fill;
            _fillOn = a.Style.FillOn;
            if (a.UsesThickness) _thickness = a.Style.Thickness;
            if (a.UsesFont) _fontSize = a.Style.FontSize;
        }
        UpdateAdorner();
        UpdateStyleUi();
        UpdateCommandStates();
    }

    private void UpdateAdorner()
    {
        if (_selected == null || !_items.Contains(_selected))
        {
            _selectionOutline.Visibility = Visibility.Collapsed;
            return;
        }
        var b = _selected.Bounds;
        double scale = ZoomTransform.ScaleX > 0 ? ZoomTransform.ScaleX : 1;
        double pad = 4 / scale;
        b.Inflate(pad, pad);
        _selectionOutline.StrokeThickness = 1.5 / scale;
        _selectionOutline.StrokeDashArray = new DoubleCollection { 4, 3 };
        Canvas.SetLeft(_selectionOutline, b.X);
        Canvas.SetTop(_selectionOutline, b.Y);
        _selectionOutline.Width = Math.Max(0, b.Width);
        _selectionOutline.Height = Math.Max(0, b.Height);
        _selectionOutline.Visibility = Visibility.Visible;
    }

    private void UpdateCommandStates()
    {
        UndoButton.IsEnabled = _undo.CanUndo;
        RedoButton.IsEnabled = _undo.CanRedo;
        DeleteButton.IsEnabled = _selected != null;
    }

    private void OnUndo(object sender, RoutedEventArgs e) => DoUndo();
    private void OnRedo(object sender, RoutedEventArgs e) => DoRedo();
    private void OnDelete(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DoUndo()
    {
        CommitText();
        FlushPendingStyle();
        _undo.Undo();
        AfterHistoryChange();
    }

    private void DoRedo()
    {
        CommitText();
        FlushPendingStyle();
        _undo.Redo();
        AfterHistoryChange();
    }

    private void AfterHistoryChange()
    {
        if (_selected != null && !_items.Contains(_selected)) _selected = null;
        if (_selected != null) Select(_selected);
        UpdateAdorner();
        UpdateCommandStates();
    }

    // ================================================================ style changes

    /// <summary>Change the style of the selected object (with undo) and of the current tool.</summary>
    private void ChangeStyle(Func<AnnotationStyle, AnnotationStyle> change, bool mergeable = false)
    {
        // current tool style
        var cur = change(CurrentStyle);
        _stroke = cur.Stroke;
        _fill = cur.Fill;
        _fillOn = cur.FillOn;
        _thickness = cur.Thickness;
        _fontSize = cur.FontSize;

        if (_textBox != null) StyleTextBox();

        var a = _selected;
        if (a != null)
        {
            if (mergeable)
            {
                if (_pendingStyleTarget != a)
                {
                    FlushPendingStyle();
                    _pendingStyleTarget = a;
                    _pendingStyleOriginal = a.Style;
                }
                a.SetStyle(change(a.Style));
                _pendingStyleTimer.Stop();
                _pendingStyleTimer.Start();
            }
            else
            {
                FlushPendingStyle();
                var before = a.Style;
                var after = change(before);
                if (before != after)
                    _undo.Do("style", () => { a.SetStyle(before); UpdateAdorner(); }, () => { a.SetStyle(after); UpdateAdorner(); });
            }
            UpdateAdorner();
        }
        UpdateStyleUi();
    }

    private void FlushPendingStyle()
    {
        _pendingStyleTimer?.Stop();
        var a = _pendingStyleTarget;
        var before = _pendingStyleOriginal;
        _pendingStyleTarget = null;
        _pendingStyleOriginal = null;
        if (a == null || before == null) return;
        var after = a.Style;
        if (before != after)
            _undo.Push("style", () => { a.SetStyle(before); UpdateAdorner(); }, () => { a.SetStyle(after); UpdateAdorner(); });
    }

    private bool SliderIsFont => _selected is TextAnnotation || (_selected == null && _tool == EditorTool.Text) || _textBox != null;

    private void OnSizeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi) return;
        double v = Math.Round(e.NewValue);
        SizeValue.Text = v.ToString("0");
        if (SliderIsFont) ChangeStyle(s => s with { FontSize = v }, mergeable: true);
        else ChangeStyle(s => s with { Thickness = v }, mergeable: true);
    }

    private void OnFillToggle(object sender, RoutedEventArgs e)
    {
        bool on = FillToggle.IsChecked == true;
        ChangeStyle(s => s with { FillOn = on });
    }

    private void UpdateStyleUi()
    {
        _updatingUi = true;
        try
        {
            StrokeSwatch.Fill = ColorUtil.Brush(_stroke);
            FillSwatch.Fill = ColorUtil.Brush(_fill);
            FillSwatch.Opacity = _fillOn ? 1 : 0.3;
            FillOffSlash.Visibility = _fillOn ? Visibility.Collapsed : Visibility.Visible;
            FillToggle.IsChecked = _fillOn;
            StrokeTarget.IsChecked = _target == ColorTarget.Stroke;
            FillTarget.IsChecked = _target == ColorTarget.Fill;

            if (SliderIsFont)
            {
                SizeLabel.Text = Strings.FontSize;
                SizeSlider.Minimum = 8;
                SizeSlider.Maximum = 120;
                SizeSlider.Value = _fontSize;
                SizeValue.Text = _fontSize.ToString("0");
            }
            else
            {
                SizeLabel.Text = Strings.Thickness;
                SizeSlider.Minimum = 1;
                SizeSlider.Maximum = 30;
                SizeSlider.Value = _thickness;
                SizeValue.Text = _thickness.ToString("0");
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
        SavedPanel.Children.Clear();
        var add = new Button { Style = (Style)FindResource("AddSwatchButton"), ToolTip = Strings.SaveCurrentColor };
        add.Click += (_, _) => SaveColor(_target == ColorTarget.Stroke ? _stroke : _fill);
        SavedPanel.Children.Add(add);
        foreach (var hex in _settings.Current.Editor.SavedColors)
            if (ColorUtil.TryParse(hex, out var c)) SavedPanel.Children.Add(MakeSwatch(c, removable: true));
    }

    /// <summary>Keep a color for later sessions (newest first, max <see cref="SettingsService.MaxSavedColors"/>).</summary>
    private void SaveColor(Color c)
    {
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
        string hex = ColorUtil.ToHex8(c);
        _settings.Current.Editor.SavedColors.RemoveAll(x => string.Equals(x, hex, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        BuildSaved();
    }

    private void BuildRecent()
    {
        RecentPanel.Children.Clear();
        foreach (var hex in _settings.Current.Editor.RecentColors)
            if (ColorUtil.TryParse(hex, out var c)) RecentPanel.Children.Add(MakeSwatch(c, removable: false));
        var vis = RecentPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentPanel.Visibility = vis;
        RecentSeparator.Visibility = vis;
    }

    private void AddRecent(Color c)
    {
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
        if (_target == ColorTarget.Stroke) ChangeStyle(s => s with { Stroke = c });
        else ChangeStyle(s => s with { Fill = c, FillOn = true });
    }

    // ================================================================ color picker

    private void OnStrokeTargetClick(object sender, RoutedEventArgs e) => OpenPicker(ColorTarget.Stroke);

    private void OnFillTargetClick(object sender, RoutedEventArgs e) => OpenPicker(ColorTarget.Fill);

    private void OpenPicker(ColorTarget target)
    {
        _target = target;
        UpdateStyleUi();
        // The window-level mouse-down just committed a previous picker: a click on the same
        // target button should close it rather than reopen.
        if ((DateTime.Now - _pickerClosedAt).TotalMilliseconds < 300 && _pickerTarget == target) return;

        CommitText();
        FlushPendingStyle();
        _pickerTarget = target;
        _pickerOriginalColor = target == ColorTarget.Stroke ? _stroke : _fill;
        _pickerOriginalFillOn = _fillOn;
        _pickerSelected = _selected;
        _pickerSelectedOriginal = _selected?.Style;

        PickerPopup.PlacementTarget = target == ColorTarget.Stroke ? StrokeTarget : FillTarget;
        Picker.Begin(_pickerOriginalColor, target == ColorTarget.Stroke ? Strings.StrokeColor : Strings.FillColor);
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
    private void ApplyPickerColor(Color c)
    {
        if (_pickerTarget == ColorTarget.Stroke) _stroke = c;
        else
        {
            _fill = c;
            _fillOn = true; // choosing a fill color implies "fill on"
        }
        if (_pickerSelected != null && _pickerSelectedOriginal != null && _items.Contains(_pickerSelected))
        {
            var orig = _pickerSelectedOriginal;
            _pickerSelected.SetStyle(_pickerTarget == ColorTarget.Stroke ? orig with { Stroke = c } : orig with { Fill = c, FillOn = true });
            UpdateAdorner();
        }
        if (_textBox != null) StyleTextBox();
        UpdateStyleUi();
    }

    private void OnPickerCommitted(Color c)
    {
        ApplyPickerColor(c);
        var a = _pickerSelected;
        var before = _pickerSelectedOriginal;
        if (a != null && before != null && _items.Contains(a) && a.Style != before)
        {
            var after = a.Style;
            _undo.Push("color", () => { a.SetStyle(before); UpdateAdorner(); }, () => { a.SetStyle(after); UpdateAdorner(); });
        }
        if (c != _pickerOriginalColor) AddRecent(c);
        ClosePicker();
    }

    private void OnPickerCancelled()
    {
        if (_pickerTarget == ColorTarget.Stroke) _stroke = _pickerOriginalColor;
        else
        {
            _fill = _pickerOriginalColor;
            _fillOn = _pickerOriginalFillOn;
        }
        if (_pickerSelected != null && _pickerSelectedOriginal != null) _pickerSelected.SetStyle(_pickerSelectedOriginal);
        if (_textBox != null) StyleTextBox();
        UpdateAdorner();
        UpdateStyleUi();
        ClosePicker();
    }

    private void ClosePicker()
    {
        PickerPopup.IsOpen = false;
        _pickerClosedAt = DateTime.Now;
        _pickerSelected = null;
        _pickerSelectedOriginal = null;
        Pad.Cursor = _tool == EditorTool.Select ? Cursors.Arrow : _tool == EditorTool.Text ? Cursors.IBeam : Cursors.Cross;
        Focus();
    }

    // ================================================================ text

    private void BeginTextEdit(Point origin, TextAnnotation? existing)
    {
        CommitText();
        Select(null);
        _editingText = existing;
        _textOrigin = origin;
        if (existing != null)
        {
            _stroke = existing.Style.Stroke;
            _fill = existing.Style.Fill;
            _fillOn = existing.Style.FillOn;
            _fontSize = existing.Style.FontSize;
            existing.Element.Visibility = Visibility.Hidden;
        }

        var tb = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            MinWidth = 60,
            FontFamily = TextAnnotation.Font,
            FontWeight = FontWeights.SemiBold,
            Text = existing?.Text ?? "",
            TextWrapping = TextWrapping.NoWrap,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        _textBox = tb;
        StyleTextBox();
        Canvas.SetLeft(tb, origin.X);
        Canvas.SetTop(tb, origin.Y);
        TextLayer.Children.Add(tb);
        UpdateStyleUi();
        Dispatcher.BeginInvoke(() =>
        {
            tb.Focus();
            Keyboard.Focus(tb);
            tb.CaretIndex = tb.Text.Length;
        }, DispatcherPriority.Input);
    }

    private void StyleTextBox()
    {
        if (_textBox == null) return;
        double scale = ZoomTransform.ScaleX > 0 ? ZoomTransform.ScaleX : 1;
        _textBox.Foreground = ColorUtil.Brush(_stroke);
        _textBox.CaretBrush = ColorUtil.Brush(Color.FromRgb(_stroke.R, _stroke.G, _stroke.B));
        _textBox.FontSize = _fontSize;
        _textBox.Background = _fillOn ? ColorUtil.Brush(_fill) : Brushes.Transparent;
        _textBox.BorderBrush = (Brush)FindResource("AccentBrush");
        _textBox.BorderThickness = new Thickness(1.5 / scale);
        _textBox.Padding = new Thickness(4, 1, 4, 1);
    }

    private void CommitText()
    {
        var tb = _textBox;
        if (tb == null) return;
        _textBox = null;
        TextLayer.Children.Remove(tb);
        string text = tb.Text.TrimEnd('\r', '\n', ' ');
        var existing = _editingText;
        _editingText = null;

        if (existing != null)
        {
            existing.Element.Visibility = Visibility.Visible;
            string oldText = existing.Text;
            var oldStyle = existing.Style;
            var newStyle = oldStyle with { Stroke = _stroke, Fill = _fill, FillOn = _fillOn, FontSize = _fontSize };
            if (text.Length == 0)
            {
                int index = _items.IndexOf(existing);
                _undo.Do("delete text", () => AddItem(existing, index), () => RemoveItem(existing));
            }
            else if (text != oldText || newStyle != oldStyle)
            {
                _undo.Do("edit text",
                    () => { existing.Text = oldText; existing.SetStyle(oldStyle); UpdateAdorner(); },
                    () => { existing.Text = text; existing.SetStyle(newStyle); UpdateAdorner(); });
            }
        }
        else if (text.Length > 0)
        {
            var a = new TextAnnotation(CurrentStyle, _textOrigin, text);
            AddItem(a, _items.Count);
            PushAdd(a);
        }
        Focus();
        UpdateStyleUi();
    }

    // ================================================================ keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);

        // Typing text: only Esc / Ctrl+Enter finish it.
        if (_textBox != null && _textBox.IsKeyboardFocusWithin)
        {
            if (key == Key.Escape || (key == Key.Enter && ctrl))
            {
                CommitText();
                e.Handled = true;
            }
            return;
        }
        // The color picker handles its own keys.
        if (PickerPopup.IsOpen && Picker.IsKeyboardFocusWithin) return;
        if (Keyboard.FocusedElement is TextBox) return;

        if (ctrl)
        {
            switch (key)
            {
                case Key.Z when shift: DoRedo(); break;
                case Key.Z: DoUndo(); break;
                case Key.Y: DoRedo(); break;
                case Key.C: Copy(); break;
                case Key.S when shift: SaveAs(); break;
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
                DeleteSelected();
                break;
            case Key.Escape:
                if (PickerPopup.IsOpen) Picker.Cancel();
                else if (_drawing != null) { var a = _drawing; _drawing = null; RemoveItem(a); if (Pad.IsMouseCaptured) Pad.ReleaseMouseCapture(); }
                else Select(null);
                break;
            case Key.V: SetTool(EditorTool.Select); break;
            case Key.B: SetTool(EditorTool.Brush); break;
            case Key.H: SetTool(EditorTool.Highlighter); break;
            case Key.L: SetTool(EditorTool.Line); break;
            case Key.A: SetTool(EditorTool.Arrow); break;
            case Key.R: SetTool(EditorTool.Rect); break;
            case Key.O: SetTool(EditorTool.Ellipse); break;
            case Key.T: SetTool(EditorTool.Text); break;
            case Key.M: SetTool(EditorTool.Mosaic); break;
            case Key.N: SetTool(EditorTool.Step); break;
            case Key.Left or Key.Right or Key.Up or Key.Down when _selected != null:
                NudgeSelected(key, shift ? 10 : 1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void NudgeSelected(Key key, double step)
    {
        var a = _selected!;
        var d = key switch
        {
            Key.Left => new Vector(-step, 0),
            Key.Right => new Vector(step, 0),
            Key.Up => new Vector(0, -step),
            _ => new Vector(0, step),
        };
        var from = a.Offset;
        var to = from + d;
        _undo.Do("nudge", () => { a.Offset = from; UpdateAdorner(); }, () => { a.Offset = to; UpdateAdorner(); });
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
        try { imgPt = Scroller.TranslatePoint(a, ExportRoot); }
        catch { imgPt = new Point(_image.Width / 2.0, _image.Height / 2.0); }

        _zoom = zoom;
        double s = zoom / _dpiScale;
        ZoomTransform.ScaleX = ZoomTransform.ScaleY = s;
        RenderOptions.SetBitmapScalingMode(BaseImage, zoom >= 0.999 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        ZoomText.Text = $"{Math.Round(zoom * 100)}%";

        Scroller.UpdateLayout();
        try
        {
            var now = ExportRoot.TranslatePoint(imgPt, Scroller);
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + now.X - a.X);
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + now.Y - a.Y);
        }
        catch
        {
            // not in the visual tree yet
        }
        UpdateAdorner();
        StyleTextBox();
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
    public BitmapSource RenderOutput()
    {
        CommitText();
        FlushPendingStyle();
        int w = _image.Width, h = _image.Height;
        ExportRoot.UpdateLayout();

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var vb = new VisualBrush(ExportRoot)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, w, h),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, w, h),
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            // The base image is drawn directly (pixel-exact); annotations come from the visual brush.
            dc.DrawImage(_image.Source, new Rect(0, 0, w, h));
            dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        BaseImage.Visibility = Visibility.Hidden; // don't draw the base image a second time through the brush
        try
        {
            rtb.Render(dv);
        }
        finally
        {
            BaseImage.Visibility = Visibility.Visible;
        }
        rtb.Freeze();
        return rtb;
    }

    private void OnCopy(object sender, RoutedEventArgs e) => Copy();
    private void OnSave(object sender, RoutedEventArgs e) => Save();
    private void OnSaveAs(object sender, RoutedEventArgs e) => SaveAs();

    private void Copy()
    {
        if (_controller.Copy(RenderOutput())) _dirty = false;
    }

    private void Save()
    {
        if (_controller.Save(RenderOutput()) != null) _dirty = false;
    }

    private void SaveAs()
    {
        if (_controller.SaveAs(RenderOutput(), this) != null) _dirty = false;
    }
}
