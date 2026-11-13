using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SnapEdit.Capture;

namespace SnapEdit.Editor;

public enum EditorTool
{
    Select,
    Brush,
    Highlighter,
    Line,
    Arrow,
    Rect,
    Ellipse,
    Text,
    Mosaic,
    Step,
}

/// <summary>Visual style of an annotation. Immutable so it can be stored in undo commands.</summary>
public sealed record AnnotationStyle(Color Stroke, Color Fill, bool FillOn, double Thickness, double FontSize);

/// <summary>
/// One drawn object. Each owns a UIElement on the drawing canvas (image pixel coordinates),
/// so it stays selectable, movable, recolorable and deletable.
/// </summary>
public abstract class Annotation
{
    protected Annotation(AnnotationStyle style) => Style = style;

    public AnnotationStyle Style { get; private set; }
    public abstract FrameworkElement Element { get; }
    protected TranslateTransform Move { get; } = new();

    private Vector _offset;

    /// <summary>Translation applied by the select/move tool.</summary>
    public Vector Offset
    {
        get => _offset;
        set
        {
            _offset = value;
            if (UsesTranslate)
            {
                Move.X = value.X;
                Move.Y = value.Y;
            }
            OnOffsetChanged();
        }
    }

    public void SetStyle(AnnotationStyle s)
    {
        Style = s;
        ApplyStyle();
    }

    protected abstract void ApplyStyle();

    /// <summary>Bounds without the offset.</summary>
    public abstract Rect LocalBounds { get; }

    public Rect Bounds
    {
        get
        {
            var b = LocalBounds;
            if (b.IsEmpty) return b;
            b.Offset(_offset);
            return b;
        }
    }

    /// <summary>Hit test in image coordinates.</summary>
    public virtual bool HitTest(Point p)
    {
        var b = Bounds;
        if (b.IsEmpty) return false;
        b.Inflate(3, 3);
        return b.Contains(p);
    }

    public virtual bool UsesStroke => true;
    public virtual bool UsesFill => false;
    public virtual bool UsesThickness => true;
    public virtual bool UsesFont => false;
    protected virtual bool UsesTranslate => true;
    protected virtual void OnOffsetChanged() { }

    protected void Init()
    {
        if (UsesTranslate) Element.RenderTransform = Move;
        ApplyStyle();
    }

    protected static Pen HitPen(double thickness) => new(Brushes.Black, thickness + 10)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    };
}

// ---------------------------------------------------------------- brush & highlighter

public sealed class StrokeAnnotation : Annotation
{
    private readonly Path _path = new()
    {
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private readonly List<Point> _points = new();
    private StreamGeometry _geometry = new();

    public bool IsHighlighter { get; }
    public override FrameworkElement Element => _path;

    public StrokeAnnotation(AnnotationStyle style, Point start, bool highlighter) : base(style)
    {
        IsHighlighter = highlighter;
        _points.Add(start);
        Rebuild();
        Init();
    }

    private double Width => IsHighlighter ? Style.Thickness * 3 + 8 : Style.Thickness;

    public void AddPoint(Point p)
    {
        var last = _points[^1];
        if ((p - last).LengthSquared < 0.6) return;
        _points.Add(p);
        Rebuild();
    }

    private void Rebuild()
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(_points[0], false, false);
            if (_points.Count == 1)
                ctx.LineTo(_points[0] + new Vector(0.01, 0), true, true); // a dot
            else
                ctx.PolyLineTo(_points.GetRange(1, _points.Count - 1), true, true);
        }
        g.Freeze();
        _geometry = g;
        _path.Data = g;
    }

    protected override void ApplyStyle()
    {
        _path.Stroke = ColorUtil.Brush(Style.Stroke);
        _path.StrokeThickness = Width;
        _path.Opacity = IsHighlighter ? 0.45 : 1.0;
        if (IsHighlighter)
        {
            _path.StrokeStartLineCap = _path.StrokeEndLineCap = PenLineCap.Square;
        }
    }

    public override Rect LocalBounds
    {
        get
        {
            var b = _geometry.Bounds;
            b.Inflate(Width / 2, Width / 2);
            return b;
        }
    }

    public override bool HitTest(Point p) => _geometry.StrokeContains(HitPen(Width), p - Offset);
}

// ---------------------------------------------------------------- line & arrow

public sealed class LineAnnotation : Annotation
{
    private readonly Path _path = new()
    {
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private Geometry _geometry = Geometry.Empty;

    public Point Start { get; private set; }
    public Point End { get; private set; }
    public bool IsArrow { get; }
    public override FrameworkElement Element => _path;

    public LineAnnotation(AnnotationStyle style, Point start, bool arrow) : base(style)
    {
        IsArrow = arrow;
        Start = End = start;
        Init();
    }

    public void SetEnd(Point end)
    {
        End = end;
        Rebuild();
    }

    private void Rebuild()
    {
        double t = Style.Thickness;
        Vector d = End - Start;
        double len = d.Length;
        var group = new GeometryGroup();
        if (!IsArrow || len < 1)
        {
            group.Children.Add(new LineGeometry(Start, End));
        }
        else
        {
            d /= len;
            var n = new Vector(-d.Y, d.X);
            double headLen = Math.Min(Math.Max(12, t * 4.2), len * 0.6 + t);
            double headW = headLen * 0.62;
            Point baseCenter = End - d * headLen;
            // shaft stops inside the head so the round cap does not poke out
            group.Children.Add(new LineGeometry(Start, End - d * (headLen * 0.6)));
            var head = new StreamGeometry();
            using (var ctx = head.Open())
            {
                ctx.BeginFigure(End, true, true);
                ctx.LineTo(baseCenter + n * headW, true, true);
                ctx.LineTo(baseCenter - n * headW, true, true);
            }
            group.Children.Add(head);
        }
        group.Freeze();
        _geometry = group;
        _path.Data = group;
    }

    protected override void ApplyStyle()
    {
        var b = ColorUtil.Brush(Style.Stroke);
        _path.Stroke = b;
        _path.Fill = IsArrow ? b : null;
        _path.StrokeThickness = Style.Thickness;
        Rebuild();
    }

    public override Rect LocalBounds
    {
        get
        {
            var b = _geometry.Bounds;
            if (b.IsEmpty) return b;
            b.Inflate(Style.Thickness, Style.Thickness);
            return b;
        }
    }

    public override bool HitTest(Point p)
    {
        var q = p - Offset;
        return _geometry.StrokeContains(HitPen(Style.Thickness), q) || (IsArrow && _geometry.FillContains(q));
    }
}

// ---------------------------------------------------------------- rectangle & ellipse

public sealed class ShapeAnnotation : Annotation
{
    private readonly Path _path = new() { StrokeLineJoin = PenLineJoin.Miter };
    private Geometry _geometry = Geometry.Empty;

    public bool IsEllipse { get; }
    public Rect Rect { get; private set; }
    public override FrameworkElement Element => _path;
    public override bool UsesFill => true;

    public ShapeAnnotation(AnnotationStyle style, Rect rect, bool ellipse) : base(style)
    {
        IsEllipse = ellipse;
        Rect = rect;
        Init();
    }

    public void SetRect(Rect r)
    {
        Rect = r;
        Rebuild();
    }

    private void Rebuild()
    {
        // Keep the stroke inside the dragged rectangle.
        var r = Rect;
        double h = Math.Min(Style.Thickness / 2, Math.Min(r.Width, r.Height) / 2);
        r.Inflate(-h, -h);
        if (r.IsEmpty || r.Width < 0 || r.Height < 0) r = new Rect(Rect.Location, new Size(0, 0));
        Geometry g = IsEllipse
            ? new EllipseGeometry(r)
            : new RectangleGeometry(r, Math.Min(Style.Thickness * 0.6, 6), Math.Min(Style.Thickness * 0.6, 6));
        g.Freeze();
        _geometry = g;
        _path.Data = g;
    }

    protected override void ApplyStyle()
    {
        _path.Stroke = ColorUtil.Brush(Style.Stroke);
        _path.StrokeThickness = Style.Thickness;
        _path.Fill = Style.FillOn ? ColorUtil.Brush(Style.Fill) : null;
        Rebuild();
    }

    public override Rect LocalBounds => Rect;

    public override bool HitTest(Point p)
    {
        var q = p - Offset;
        if (Style.FillOn && _geometry.FillContains(q)) return true;
        return _geometry.StrokeContains(HitPen(Style.Thickness), q);
    }
}

// ---------------------------------------------------------------- text

public sealed class TextAnnotation : Annotation
{
    private readonly Border _border = new() { Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4) };
    private readonly TextBlock _text = new()
    {
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.NoWrap,
    };

    public Point Origin { get; }
    public string Text { get => _text.Text; set { _text.Text = value; } }
    public override FrameworkElement Element => _border;
    public override bool UsesFill => true;
    public override bool UsesThickness => false;
    public override bool UsesFont => true;

    public static readonly FontFamily Font = new("Segoe UI, Leelawadee UI, Tahoma");

    public TextAnnotation(AnnotationStyle style, Point origin, string text) : base(style)
    {
        Origin = origin;
        _text.FontFamily = Font;
        _text.Text = text;
        _border.Child = _text;
        Canvas.SetLeft(_border, origin.X);
        Canvas.SetTop(_border, origin.Y);
        Init();
    }

    protected override void ApplyStyle()
    {
        _text.Foreground = ColorUtil.Brush(Style.Stroke);
        _text.FontSize = Style.FontSize;
        _border.Background = Style.FillOn ? ColorUtil.Brush(Style.Fill) : null;
    }

    public override Rect LocalBounds
    {
        get
        {
            _border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return new Rect(Origin, _border.DesiredSize);
        }
    }
}

// ---------------------------------------------------------------- numbered step

public sealed class StepAnnotation : Annotation
{
    private readonly Grid _grid = new();
    private readonly Ellipse _circle = new() { Stroke = Brushes.White, StrokeThickness = 2 };
    private readonly TextBlock _label = new()
    {
        FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontFamily = TextAnnotation.Font,
    };

    public Point Center { get; }
    public int Number { get; }
    public override FrameworkElement Element => _grid;

    public StepAnnotation(AnnotationStyle style, Point center, int number) : base(style)
    {
        Center = center;
        Number = number;
        _label.Text = number.ToString();
        _grid.Children.Add(_circle);
        _grid.Children.Add(_label);
        Init();
    }

    private double Diameter => 20 + Style.Thickness * 3;

    protected override void ApplyStyle()
    {
        double d = Diameter;
        _grid.Width = _grid.Height = d;
        Canvas.SetLeft(_grid, Center.X - d / 2);
        Canvas.SetTop(_grid, Center.Y - d / 2);
        _circle.Fill = ColorUtil.Brush(Style.Stroke);
        _circle.StrokeThickness = Math.Max(1.5, d / 16);
        var opaque = Style.Stroke;
        opaque.A = 255;
        _label.Foreground = ColorUtil.Brush(ColorUtil.Contrast(opaque));
        _label.FontSize = d * (Number >= 10 ? 0.42 : 0.52);
    }

    public override Rect LocalBounds
    {
        get
        {
            double d = Diameter;
            return new Rect(Center.X - d / 2, Center.Y - d / 2, d, d);
        }
    }

    public override bool HitTest(Point p) => (p - Offset - Center).Length <= Diameter / 2 + 3;
}

// ---------------------------------------------------------------- mosaic (redaction)

/// <summary>
/// Pixelates a region of the base image. The pixelated pixels are a real bitmap drawn over the image,
/// so the exported file never contains the original pixels of that region.
/// </summary>
public sealed class MosaicAnnotation : Annotation
{
    private readonly Image _image = new() { Stretch = Stretch.None };
    private readonly PixelImage _source;
    private PxRect _baked = default;

    public Rect Rect { get; private set; }
    public override FrameworkElement Element => _image;
    public override bool UsesStroke => false;
    protected override bool UsesTranslate => false;

    public MosaicAnnotation(AnnotationStyle style, Rect rect, PixelImage source) : base(style)
    {
        _source = source;
        Rect = rect;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        Init();
    }

    public void SetRect(Rect r)
    {
        Rect = r;
        Regenerate();
    }

    private int BlockSize => (int)Math.Max(4, Math.Round(Style.Thickness * 2 + 4));

    protected override void ApplyStyle()
    {
        _baked = default;
        Regenerate();
    }

    protected override void OnOffsetChanged() => Regenerate();

    /// <summary>Effective pixel rectangle (integer, clamped to the image).</summary>
    public PxRect PixelRect
    {
        get
        {
            var r = Rect;
            r.Offset(Offset);
            int x1 = (int)Math.Round(r.Left), y1 = (int)Math.Round(r.Top);
            int x2 = (int)Math.Round(r.Right), y2 = (int)Math.Round(r.Bottom);
            return PxRect.FromPoints(x1, y1, x2, y2).Intersect(new PxRect(0, 0, _source.Width, _source.Height));
        }
    }

    private void Regenerate()
    {
        var pr = PixelRect;
        if (pr.IsEmpty)
        {
            _image.Source = null;
            _baked = pr;
            return;
        }
        if (pr == _baked && _image.Source != null) return;
        _baked = pr;

        int bs = BlockSize;
        int w = pr.Width, h = pr.Height, stride = w * 4;
        var dst = new byte[stride * h];
        byte[] src = _source.Pixels;
        int sstride = _source.Stride;
        for (int by = 0; by < h; by += bs)
        {
            int bh = Math.Min(bs, h - by);
            for (int bx = 0; bx < w; bx += bs)
            {
                int bw = Math.Min(bs, w - bx);
                long sb = 0, sg = 0, sr = 0;
                for (int y = 0; y < bh; y++)
                {
                    int si = (pr.Y + by + y) * sstride + (pr.X + bx) * 4;
                    for (int x = 0; x < bw; x++, si += 4)
                    {
                        sb += src[si];
                        sg += src[si + 1];
                        sr += src[si + 2];
                    }
                }
                int n = bw * bh;
                byte b = (byte)(sb / n), g = (byte)(sg / n), r = (byte)(sr / n);
                for (int y = 0; y < bh; y++)
                {
                    int di = (by + y) * stride + bx * 4;
                    for (int x = 0; x < bw; x++, di += 4)
                    {
                        dst[di] = b;
                        dst[di + 1] = g;
                        dst[di + 2] = r;
                        dst[di + 3] = 255;
                    }
                }
            }
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, dst, stride);
        bmp.Freeze();
        _image.Source = bmp;
        _image.Width = w;
        _image.Height = h;
        Canvas.SetLeft(_image, pr.X);
        Canvas.SetTop(_image, pr.Y);
    }

    public override Rect LocalBounds => Rect;
}
