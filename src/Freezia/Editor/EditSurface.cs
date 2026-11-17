using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Freezia.Capture;

namespace Freezia.Editor;

/// <summary>
/// The editing core shared by the editor window and the capture overlay: the image, the drawn objects,
/// all drawing tools, selection / endpoint handles, the eraser, text editing, undo and export.
/// Coordinates are image pixels (1 DIP inside <see cref="ExportRoot"/> = 1 image pixel).
/// The host feeds pointer input (<see cref="PointerDown"/> / <see cref="PointerMove"/> / <see cref="PointerUp"/>)
/// and tells the surface how large one image pixel is on screen (<see cref="ViewScale"/>).
/// </summary>
public sealed class EditSurface : UserControl
{
    private PixelImage _image;
    private readonly UndoManager _undo = new();
    private readonly List<Annotation> _items = new();

    // current style
    private Color _stroke, _fill;
    private bool _fillOn;
    private double _thickness, _fontSize;

    private EditorTool _tool = EditorTool.None;
    private Annotation? _selected;
    private readonly Rectangle _selectionOutline;

    // line / arrow endpoint handles: 1 = start, 2 = end
    private readonly Ellipse _handleStart, _handleEnd;
    private int _endpointDrag;
    private Point _endpointFromStart, _endpointFromEnd;

    // text tool: pressing on a text drags it, a click without dragging edits it
    private TextAnnotation? _textClick;

    // eraser
    private readonly Ellipse _eraserCursor;
    private bool _erasing;
    private readonly List<Point> _erasePoints = new();
    private readonly Dictionary<Annotation, Geometry> _eraseApplied = new();

    // drag state
    private Annotation? _drawing;
    private Point _dragStart;
    private bool _moving;
    private Vector _moveStartOffset;

    // text editing
    private TextBox? _textBox;
    private TextAnnotation? _editingText;
    private Point _textOrigin;

    // slider edits on a selected object are merged into one undo step
    private Annotation? _pendingStyleTarget;
    private AnnotationStyle? _pendingStyleOriginal;
    private readonly DispatcherTimer _pendingStyleTimer;

    // live color preview (color picker open)
    private Annotation? _previewSelected;
    private AnnotationStyle? _previewOriginal;

    private double _viewScale = 1;

    public Grid ExportRoot { get; }
    public Image BaseImage { get; }
    public Path SpotDim { get; }
    public Canvas DrawCanvas { get; }
    public Canvas TextLayer { get; }

    /// <summary>Not exported, not hit-testable: selection outline, handles, eraser cursor (hosts add their own, e.g. crop).</summary>
    public Canvas AdornerLayer { get; }

    /// <summary>Element that captures the mouse during drags and shows the tool cursor (the host's input area).</summary>
    public FrameworkElement? InputElement { get; set; }

    public UndoManager Undo => _undo;
    public IReadOnlyList<Annotation> Items => _items;
    public Annotation? Selected => _selected;
    public EditorTool Tool => _tool;
    public bool IsEditingText => _textBox != null;
    public TextBox? ActiveTextBox => _textBox;
    public bool IsDragging => _drawing != null || _moving || _erasing || _endpointDrag != 0;
    public PixelImage Source => _image;

    public Color StrokeColor => _stroke;
    public Color FillColor => _fill;
    public bool FillOn => _fillOn;
    public double Thickness => _thickness;
    public double FontSizeValue => _fontSize;
    public AnnotationStyle CurrentStyle => new(_stroke, _fill, _fillOn, _thickness, _fontSize);

    /// <summary>Tool, selection, style or text-editing state changed: toolbars refresh.</summary>
    public event Action? StateChanged;

    /// <summary>Undo/redo availability or the item list changed.</summary>
    public event Action? CommandsChanged;

    /// <summary>Pointer position in image pixels (for status bars).</summary>
    public event Action<Point>? PointerMoved;

    /// <summary>Text editing started (true) or ended (false).</summary>
    public event Action<bool>? TextEditingChanged;

    /// <summary>
    /// Called first on every pointer press; return true to consume it (the color picker uses this for the
    /// eyedropper and to close itself without drawing).
    /// </summary>
    public Func<Point, bool>? PreviewPointerDown { get; set; }

    /// <summary>Overrides the tool cursor (e.g. eyedropper).</summary>
    public Cursor? CursorOverride
    {
        get => _cursorOverride;
        set { _cursorOverride = value; RefreshCursor(); }
    }
    private Cursor? _cursorOverride;

    public EditSurface(PixelImage image, AnnotationStyle style)
    {
        _image = image;
        _stroke = style.Stroke;
        _fill = style.Fill;
        _fillOn = style.FillOn;
        _thickness = style.Thickness;
        _fontSize = style.FontSize;

        BaseImage = new Image { Stretch = Stretch.None };
        SpotDim = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), IsHitTestVisible = false };
        DrawCanvas = new Canvas();
        TextLayer = new Canvas();
        ExportRoot = new Grid { ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        ExportRoot.Children.Add(BaseImage);
        ExportRoot.Children.Add(SpotDim);
        ExportRoot.Children.Add(DrawCanvas);
        ExportRoot.Children.Add(TextLayer);
        AdornerLayer = new Canvas { IsHitTestVisible = false };
        var root = new Grid();
        root.Children.Add(ExportRoot);
        root.Children.Add(AdornerLayer);
        Content = root;
        SetImage(image);

        _selectionOutline = new Rectangle
        {
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Visibility = Visibility.Collapsed,
        };
        AdornerLayer.Children.Add(_selectionOutline);
        _handleStart = MakeHandle();
        _handleEnd = MakeHandle();
        AdornerLayer.Children.Add(_handleStart);
        AdornerLayer.Children.Add(_handleEnd);
        _eraserCursor = new Ellipse
        {
            Stroke = (Brush)FindResource("AccentBrush"),
            Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            Visibility = Visibility.Collapsed,
        };
        AdornerLayer.Children.Add(_eraserCursor);

        _pendingStyleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _pendingStyleTimer.Tick += (_, _) => FlushPendingStyle();
        _undo.Changed += (_, _) => CommandsChanged?.Invoke();
    }

    private void SetImage(PixelImage img)
    {
        _image = img;
        BaseImage.Source = img.Source;
        ExportRoot.Width = img.Width;
        ExportRoot.Height = img.Height;
    }

    /// <summary>DIPs per image pixel on screen (zoom). Handles and outlines keep a constant on-screen size.</summary>
    public double ViewScale
    {
        get => _viewScale;
        set
        {
            _viewScale = value > 0 ? value : 1;
            UpdateAdorner();
            StyleTextBox();
        }
    }

    private void RaiseState() => StateChanged?.Invoke();

    private void FocusHost()
    {
        if (Window.GetWindow(this) is Window w) w.Focus();
    }

    private void CaptureInput() => InputElement?.CaptureMouse();

    private void ReleaseInput()
    {
        if (InputElement?.IsMouseCaptured == true) InputElement.ReleaseMouseCapture();
    }

    private Cursor ToolCursor => _tool switch
    {
        EditorTool.Select => Cursors.Arrow,
        EditorTool.Text => Cursors.IBeam,
        EditorTool.None => Cursors.Arrow,
        _ => Cursors.Cross,
    };

    private void SetCursor(Cursor? c)
    {
        if (InputElement != null) InputElement.Cursor = _cursorOverride ?? c;
    }

    public void RefreshCursor() => SetCursor(ToolCursor);

    // ================================================================ tools

    /// <summary>Tool keys shared by the editor and the capture overlay.</summary>
    public static EditorTool? ToolForKey(Key key) => key switch
    {
        Key.V => EditorTool.Select,
        Key.B => EditorTool.Brush,
        Key.H => EditorTool.Highlighter,
        Key.L => EditorTool.Line,
        Key.A => EditorTool.Arrow,
        Key.R => EditorTool.Rect,
        Key.O => EditorTool.Ellipse,
        Key.T => EditorTool.Text,
        Key.M => EditorTool.Mosaic,
        Key.N => EditorTool.Step,
        Key.E => EditorTool.Eraser,
        Key.U => EditorTool.Blur,
        Key.S => EditorTool.Spotlight,
        _ => null,
    };

    public void SetTool(EditorTool tool)
    {
        if (_tool == tool) { RaiseState(); return; }
        CommitText();
        FlushPendingStyle();
        _tool = tool;
        if (tool != EditorTool.Select) Select(null);
        RefreshCursor();
        if (tool != EditorTool.Eraser) _eraserCursor.Visibility = Visibility.Collapsed;
        RaiseState();
    }

    private bool InsideImage(Point p) => p.X >= 0 && p.Y >= 0 && p.X < _image.Width && p.Y < _image.Height;

    // ================================================================ pointer

    public void PointerDown(Point p, int clickCount)
    {
        if (PreviewPointerDown?.Invoke(p) == true) return;
        FocusHost();

        if (_textBox != null)
        {
            // clicking outside the text box finishes the text
            CommitText();
            if (_tool == EditorTool.Text) return;
        }

        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _dragStart = p;

        // drag an endpoint of the selected line / arrow (length and direction)
        if (_tool is EditorTool.Select or EditorTool.Line or EditorTool.Arrow && HandleAt(p) is int handle and > 0)
        {
            var line = (LineAnnotation)_selected!;
            _endpointDrag = handle;
            _endpointFromStart = line.Start;
            _endpointFromEnd = line.End;
            CaptureInput();
            return;
        }

        switch (_tool)
        {
            case EditorTool.Select:
            {
                var hit = HitTest(p);
                Select(hit);
                if (hit != null)
                {
                    if (clickCount == 2 && hit is TextAnnotation ta)
                    {
                        BeginTextEdit(ta.Origin + ta.Offset, ta);
                        break;
                    }
                    _moving = true;
                    _moveStartOffset = hit.Offset;
                    CaptureInput();
                }
                break;
            }
            case EditorTool.Brush:
            case EditorTool.Highlighter:
                StartDrawing(new StrokeAnnotation(CurrentStyle, p, _tool == EditorTool.Highlighter));
                break;
            case EditorTool.Line:
            case EditorTool.Arrow:
                Select(null);
                StartDrawing(new LineAnnotation(CurrentStyle, p, _tool == EditorTool.Arrow));
                break;
            case EditorTool.Rect:
            case EditorTool.Ellipse:
                StartDrawing(new ShapeAnnotation(CurrentStyle, new Rect(p, p), _tool == EditorTool.Ellipse));
                break;
            case EditorTool.Mosaic:
                StartDrawing(new MosaicAnnotation(CurrentStyle, new Rect(p, p), _image));
                break;
            case EditorTool.Blur:
                StartDrawing(new MosaicAnnotation(CurrentStyle, new Rect(p, p), _image, RedactMode.Blur));
                break;
            case EditorTool.Spotlight:
                StartDrawing(new SpotlightAnnotation(CurrentStyle, new Rect(p, p), ellipse: shift));
                break;
            case EditorTool.Text:
            {
                if (HitTest(p) is TextAnnotation existing)
                {
                    // drag = move it; click without dragging = edit it (see FinishDrag)
                    Select(existing);
                    _textClick = existing;
                    _moving = true;
                    _moveStartOffset = existing.Offset;
                    CaptureInput();
                }
                else if (InsideImage(p))
                    BeginTextEdit(p, null);
                break;
            }
            case EditorTool.Eraser:
                _erasing = true;
                _erasePoints.Clear();
                _erasePoints.Add(p);
                _eraseApplied.Clear();
                UpdateErase();
                CaptureInput();
                break;
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
    }

    private void StartDrawing(Annotation a)
    {
        _drawing = a;
        AddItem(a, _items.Count);
        CaptureInput();
    }

    public void PointerMove(Point p)
    {
        PointerMoved?.Invoke(p);

        if (_tool == EditorTool.Eraser) UpdateEraserCursor(p);
        if (_erasing)
        {
            if ((p - _erasePoints[^1]).LengthSquared >= 1)
            {
                _erasePoints.Add(p);
                UpdateErase();
            }
            return;
        }

        if (_endpointDrag != 0 && _selected is LineAnnotation dl)
        {
            var off = dl.Offset;
            Point start = dl.Start + off, end = dl.End + off;
            bool snap = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (_endpointDrag == 1) start = snap ? SnapAngle(end, p) : p;
            else end = snap ? SnapAngle(start, p) : p;
            dl.SetPoints(start - off, end - off);
            UpdateAdorner();
            return;
        }
        if (_moving && _selected != null)
        {
            _selected.Offset = _moveStartOffset + (p - _dragStart);
            UpdateAdorner();
            return;
        }
        if (_drawing == null)
        {
            if (_cursorOverride == null && HoverCursor(p) is Cursor c) SetCursor(c);
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
            case SpotlightAnnotation sp:
                sp.SetRect(DragRect(_dragStart, p, false));
                UpdateSpotlights();
                break;
        }
    }

    public void PointerUp(Point p)
    {
        if (_erasing)
        {
            FinishDrag();
            return;
        }
        if (_drawing != null || _moving || _endpointDrag != 0)
        {
            if (_drawing is StrokeAnnotation s) s.AddPoint(p);
            FinishDrag();
        }
    }

    /// <summary>The host lost the mouse capture: end whatever drag is running.</summary>
    public void PointerCaptureLost()
    {
        if (IsDragging) FinishDrag();
    }

    public void PointerLeave() => _eraserCursor.Visibility = Visibility.Collapsed;

    private void FinishDrag()
    {
        if (_erasing)
        {
            _erasing = false;
            var applied = new Dictionary<Annotation, Geometry>(_eraseApplied);
            _eraseApplied.Clear();
            if (applied.Count > 0)
            {
                _undo.Push("erase",
                    () => { foreach (var (a, g) in applied) a.RemoveErasure(g); UpdateAdorner(); },
                    () => { foreach (var (a, g) in applied) a.AddErasure(g); UpdateAdorner(); });
            }
        }
        if (_endpointDrag != 0)
        {
            _endpointDrag = 0;
            if (_selected is LineAnnotation l)
            {
                Point s0 = _endpointFromStart, e0 = _endpointFromEnd, s1 = l.Start, e1 = l.End;
                if (s0 != s1 || e0 != e1)
                    _undo.Push("reshape", () => { l.SetPoints(s0, e0); UpdateAdorner(); }, () => { l.SetPoints(s1, e1); UpdateAdorner(); });
            }
        }
        TextAnnotation? editText = null;
        if (_moving && _selected != null)
        {
            var a = _selected;
            var from = _moveStartOffset;
            var to = a.Offset;
            _moving = false;
            if (_textClick == a && (to - from).Length * _viewScale < 3)
            {
                // a click on a text (text tool) edits it
                a.Offset = from;
                editText = _textClick;
            }
            else if ((to - from).LengthSquared > 0.01)
                _undo.Push("move", () => { a.Offset = from; UpdateAdorner(); }, () => { a.Offset = to; UpdateAdorner(); });
        }
        _moving = false;
        _textClick = null;
        UpdateSpotlights();

        if (_drawing != null)
        {
            var a = _drawing;
            _drawing = null;
            bool tooSmall = a switch
            {
                LineAnnotation l => (l.End - l.Start).Length < 2,
                ShapeAnnotation sh => sh.Rect.Width < 2 && sh.Rect.Height < 2,
                MosaicAnnotation m => m.Rect.Width < 2 || m.Rect.Height < 2,
                SpotlightAnnotation sp => sp.Rect.Width < 2 || sp.Rect.Height < 2,
                _ => false,
            };
            if (tooSmall) RemoveItem(a);
            else
            {
                PushAdd(a);
                // keep a new line / arrow selected so its ends can be dragged right away
                if (a is LineAnnotation) Select(a);
            }
        }
        ReleaseInput();
        if (editText != null) BeginTextEdit(editText.Origin + editText.Offset, editText);
        CommandsChanged?.Invoke();
    }

    /// <summary>
    /// Esc inside the surface, one step at a time: eraser stroke, endpoint drag, move, drawing in progress,
    /// text editing, selection. Returns false when there was nothing to cancel.
    /// </summary>
    public bool CancelStep()
    {
        if (_erasing)
        {
            foreach (var (a, g) in _eraseApplied) a.RemoveErasure(g);
            _eraseApplied.Clear();
            _erasing = false;
            ReleaseInput();
            return true;
        }
        if (_endpointDrag != 0 && _selected is LineAnnotation el)
        {
            el.SetPoints(_endpointFromStart, _endpointFromEnd);
            _endpointDrag = 0;
            UpdateAdorner();
            ReleaseInput();
            return true;
        }
        if (_moving && _selected != null)
        {
            // put the object back where the drag started
            _selected.Offset = _moveStartOffset;
            _moving = false;
            _textClick = null;
            UpdateAdorner();
            ReleaseInput();
            return true;
        }
        if (_drawing != null)
        {
            var a = _drawing;
            _drawing = null;
            RemoveItem(a);
            ReleaseInput();
            return true;
        }
        if (_textBox != null)
        {
            CommitText();
            return true;
        }
        if (_selected != null)
        {
            Select(null);
            return true;
        }
        return false;
    }

    private Ellipse MakeHandle() => new()
    {
        Fill = Brushes.White,
        Stroke = (Brush)FindResource("AccentBrush"),
        Visibility = Visibility.Collapsed,
    };

    /// <summary>Which endpoint handle of the selected line / arrow is under <paramref name="p"/> (0 = none).</summary>
    private int HandleAt(Point p)
    {
        if (_selected is not LineAnnotation l || !_items.Contains(l) || l.Element.Visibility != Visibility.Visible) return 0;
        double r = 9 / _viewScale;
        if ((l.End + l.Offset - p).Length <= r) return 2;
        if ((l.Start + l.Offset - p).Length <= r) return 1;
        return 0;
    }

    /// <summary>Cursor while hovering (no button down); null keeps the tool's cursor.</summary>
    private Cursor? HoverCursor(Point p)
    {
        if (_tool is EditorTool.Select or EditorTool.Line or EditorTool.Arrow && HandleAt(p) > 0) return Cursors.Hand;
        return _tool switch
        {
            EditorTool.Select => HitTest(p) != null ? Cursors.SizeAll : Cursors.Arrow,
            EditorTool.Text => HitTest(p) is TextAnnotation ? Cursors.SizeAll : Cursors.IBeam,
            EditorTool.Line or EditorTool.Arrow => Cursors.Cross,
            _ => null,
        };
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

    public Annotation? HitTest(Point p)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var a = _items[i];
            if (a.Element.Visibility == Visibility.Visible && a.HitTest(p)) return a;
        }
        return null;
    }

    // ================================================================ items + undo

    public void AddItem(Annotation a, int index)
    {
        index = Math.Clamp(index, 0, _items.Count);
        _items.Insert(index, a);
        DrawCanvas.Children.Insert(index, a.Element);
        if (a is SpotlightAnnotation) UpdateSpotlights();
        CommandsChanged?.Invoke();
    }

    public void RemoveItem(Annotation a)
    {
        if (_selected == a) Select(null);
        _items.Remove(a);
        DrawCanvas.Children.Remove(a.Element);
        if (a is SpotlightAnnotation) UpdateSpotlights();
        CommandsChanged?.Invoke();
    }

    public void PushAdd(Annotation a)
    {
        int index = _items.IndexOf(a);
        _undo.Push("add", () => RemoveItem(a), () => AddItem(a, index));
    }

    // ================================================================ eraser + clear all

    public double EraserSize => _thickness * 3 + 6;

    private void UpdateEraserCursor(Point p)
    {
        double d = EraserSize;
        _eraserCursor.Width = _eraserCursor.Height = d;
        _eraserCursor.StrokeThickness = 1.5 / _viewScale;
        Canvas.SetLeft(_eraserCursor, p.X - d / 2);
        Canvas.SetTop(_eraserCursor, p.Y - d / 2);
        _eraserCursor.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Re-apply the whole current eraser stroke to every drawing it touches. Each object keeps the
    /// erased shape in its own coordinates (a clip), so the screenshot itself is never erased.
    /// </summary>
    private void UpdateErase()
    {
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(_erasePoints[0], false, false);
            if (_erasePoints.Count == 1) ctx.LineTo(_erasePoints[0] + new Vector(0.01, 0), true, true);
            else ctx.PolyLineTo(_erasePoints.GetRange(1, _erasePoints.Count - 1), true, true);
        }
        var pen = new Pen(Brushes.Black, EraserSize)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        Geometry shape = line.GetWidenedPathGeometry(pen);
        shape.Freeze();
        var bounds = shape.Bounds;

        foreach (var a in _items)
        {
            bool touched = _eraseApplied.ContainsKey(a) || (a.Element.Visibility == Visibility.Visible && a.Bounds.IntersectsWith(bounds));
            if (!touched) continue;
            if (_eraseApplied.TryGetValue(a, out var old)) a.RemoveErasure(old);
            var local = a.ToLocal(shape);
            a.AddErasure(local);
            _eraseApplied[a] = local;
        }
    }

    /// <summary>Remove every drawing in one undoable step.</summary>
    public void ClearAll()
    {
        CommitText();
        FlushPendingStyle();
        if (_items.Count == 0) return;
        var snapshot = _items.ToList();
        Select(null);
        _undo.Do("clear all",
            () => { for (int i = 0; i < snapshot.Count; i++) AddItem(snapshot[i], i); },
            () => { foreach (var a in snapshot) RemoveItem(a); });
    }

    public void DeleteSelected()
    {
        if (_selected == null) return;
        FlushPendingStyle();
        var a = _selected;
        int index = _items.IndexOf(a);
        _undo.Do("delete", () => AddItem(a, index), () => RemoveItem(a));
    }

    public void Select(Annotation? a)
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
        RaiseState();
        CommandsChanged?.Invoke();
    }

    /// <summary>One dim layer for all spotlights: the image minus the union of their bright areas.</summary>
    public void UpdateSpotlights()
    {
        var spots = _items.OfType<SpotlightAnnotation>().Where(s => s.Element.Visibility == Visibility.Visible).ToList();
        if (spots.Count == 0)
        {
            SpotDim.Data = null;
            return;
        }
        var holes = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var s in spots) holes.Children.Add(s.HoleGeometry);
        var dim = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, _image.Width, _image.Height)), holes);
        dim.Freeze();
        SpotDim.Data = dim;
    }

    public void UpdateAdorner()
    {
        UpdateSpotlights();
        _handleStart.Visibility = _handleEnd.Visibility = Visibility.Collapsed;
        if (_selected == null || !_items.Contains(_selected))
        {
            _selectionOutline.Visibility = Visibility.Collapsed;
            return;
        }
        double scale = _viewScale;
        if (_selected is LineAnnotation line)
        {
            // a line / arrow shows a handle on each end instead of a box
            _selectionOutline.Visibility = Visibility.Collapsed;
            PlaceHandle(_handleStart, line.Start + line.Offset, scale);
            PlaceHandle(_handleEnd, line.End + line.Offset, scale);
            return;
        }
        var b = _selected.Bounds;
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

    private static void PlaceHandle(Ellipse h, Point at, double scale)
    {
        double d = 11 / scale;
        h.Width = h.Height = d;
        h.StrokeThickness = 2 / scale;
        Canvas.SetLeft(h, at.X - d / 2);
        Canvas.SetTop(h, at.Y - d / 2);
        h.Visibility = Visibility.Visible;
    }

    public void DoUndo()
    {
        CommitText();
        FlushPendingStyle();
        _undo.Undo();
        AfterHistoryChange();
    }

    public void DoRedo()
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
        CommandsChanged?.Invoke();
    }

    public void NudgeSelected(Key key, double step)
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

    // ================================================================ image replacement (crop / resize / hand-over)

    /// <summary>Use a new base image; drawings move by <paramref name="shift"/> so they stay on the same pixels.</summary>
    public void ReplaceImage(PixelImage img, Vector shift)
    {
        foreach (var a in _items) a.Rebase(shift, img);
        SetImage(img);
        Select(null);
        UpdateSpotlights();
    }

    /// <summary>Remove all drawings from this surface (no undo) so another surface can take them over.</summary>
    public List<Annotation> DetachItems()
    {
        CommitText();
        FlushPendingStyle();
        Select(null);
        var list = _items.ToList();
        foreach (var a in list) DrawCanvas.Children.Remove(a.Element);
        _items.Clear();
        UpdateSpotlights();
        CommandsChanged?.Invoke();
        return list;
    }

    /// <summary>Take over drawings from another surface, moved by <paramref name="shift"/> (no undo step).</summary>
    public void AttachItems(IEnumerable<Annotation> items, Vector shift)
    {
        foreach (var a in items)
        {
            a.Rebase(shift, _image);
            _items.Add(a);
            DrawCanvas.Children.Add(a.Element);
        }
        UpdateSpotlights();
        CommandsChanged?.Invoke();
    }

    // ================================================================ style changes

    /// <summary>Change the style of the selected object (with undo) and of the current tool.</summary>
    public void ChangeStyle(Func<AnnotationStyle, AnnotationStyle> change, bool mergeable = false)
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
        RaiseState();
    }

    public void FlushPendingStyle()
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

    /// <summary>The size slider edits the font size (text) rather than the line thickness.</summary>
    public bool SliderIsFont => _selected is TextAnnotation || (_selected == null && _tool == EditorTool.Text) || _textBox != null;

    // ---- live color preview (color picker) ----

    /// <summary>Start a color preview: remembers the selected object's style for cancel / undo.</summary>
    public void BeginColorPreview()
    {
        CommitText();
        FlushPendingStyle();
        _previewSelected = _selected;
        _previewOriginal = _selected?.Style;
    }

    /// <summary>Preview a color on the tool style and the selected object (no undo entries).</summary>
    public void PreviewColor(bool stroke, Color c)
    {
        if (stroke) _stroke = c;
        else
        {
            _fill = c;
            _fillOn = true; // choosing a fill color implies "fill on"
        }
        if (_previewSelected != null && _previewOriginal != null && _items.Contains(_previewSelected))
        {
            var orig = _previewOriginal;
            _previewSelected.SetStyle(stroke ? orig with { Stroke = c } : orig with { Fill = c, FillOn = true });
            UpdateAdorner();
        }
        if (_textBox != null) StyleTextBox();
        RaiseState();
    }

    /// <summary>Keep the previewed color: one undo step for the selected object.</summary>
    public void CommitColorPreview()
    {
        var a = _previewSelected;
        var before = _previewOriginal;
        if (a != null && before != null && _items.Contains(a) && a.Style != before)
        {
            var after = a.Style;
            _undo.Push("color", () => { a.SetStyle(before); UpdateAdorner(); }, () => { a.SetStyle(after); UpdateAdorner(); });
        }
        _previewSelected = null;
        _previewOriginal = null;
    }

    /// <summary>Restore the tool style and the selected object after a cancelled preview.</summary>
    public void CancelColorPreview(bool stroke, Color original, bool originalFillOn)
    {
        if (stroke) _stroke = original;
        else
        {
            _fill = original;
            _fillOn = originalFillOn;
        }
        if (_previewSelected != null && _previewOriginal != null) _previewSelected.SetStyle(_previewOriginal);
        _previewSelected = null;
        _previewOriginal = null;
        if (_textBox != null) StyleTextBox();
        UpdateAdorner();
        RaiseState();
    }

    // ================================================================ text

    public void BeginTextEdit(Point origin, TextAnnotation? existing)
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
        RaiseState();
        TextEditingChanged?.Invoke(true);
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
        _textBox.Foreground = ColorUtil.Brush(_stroke);
        _textBox.CaretBrush = ColorUtil.Brush(Color.FromRgb(_stroke.R, _stroke.G, _stroke.B));
        _textBox.FontSize = _fontSize;
        _textBox.Background = _fillOn ? ColorUtil.Brush(_fill) : Brushes.Transparent;
        _textBox.BorderBrush = (Brush)FindResource("AccentBrush");
        _textBox.BorderThickness = new Thickness(1.5 / _viewScale);
        _textBox.Padding = new Thickness(4, 1, 4, 1);
    }

    public void CommitText()
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
        FocusHost();
        RaiseState();
        TextEditingChanged?.Invoke(false);
    }

    // ================================================================ export

    /// <summary>
    /// Render image + drawings at the exact pixel size (96 DPI, 1 DIP = 1 px). <paramref name="region"/> limits
    /// the output to that part of the image (the capture overlay renders only the selection).
    /// </summary>
    public BitmapSource RenderOutput(Int32Rect? region = null)
    {
        CommitText();
        FlushPendingStyle();
        var r = region ?? new Int32Rect(0, 0, _image.Width, _image.Height);
        int w = Math.Max(1, r.Width), h = Math.Max(1, r.Height);
        ExportRoot.UpdateLayout();

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var vb = new VisualBrush(ExportRoot)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(r.X, r.Y, w, h),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, w, h),
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            // The base image is drawn directly (pixel-exact); annotations come from the visual brush.
            dc.DrawImage(_image.Source, new Rect(-r.X, -r.Y, _image.Width, _image.Height));
            dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var vis = BaseImage.Visibility;
        BaseImage.Visibility = Visibility.Hidden; // don't draw the base image a second time through the brush
        try
        {
            rtb.Render(dv);
        }
        finally
        {
            BaseImage.Visibility = vis;
        }
        rtb.Freeze();
        return rtb;
    }
}
