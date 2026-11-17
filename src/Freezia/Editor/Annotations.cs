using System;
using System.Buffers;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Freezia.Capture;

namespace Freezia.Editor;

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
    Eraser,
    Blur,
    Spotlight,
    Crop,
    /// <summary>No tool (capture overlay): the selection can be moved / resized, nothing is drawn.</summary>
    None,
}

public enum RedactMode
{
    Pixelate,
    Blur,
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

    /// <summary>Hit test in image coordinates (erased parts don't count).</summary>
    public bool HitTest(Point p)
    {
        if (!HitTestCore(p)) return false;
        if (_erasures.Count == 0) return true;
        // the point must lie in a part of the object that is still visible
        return Element.Clip?.FillContains(ToLocal(p)) ?? true;
    }

    protected virtual bool HitTestCore(Point p)
    {
        var b = Bounds;
        if (b.IsEmpty) return false;
        b.Inflate(3, 3);
        return b.Contains(p);
    }

    // ---------------------------------------------------------------- eraser support

    private readonly List<Geometry> _erasures = new();

    /// <summary>Erased regions, in the element's own (pre-transform) coordinates, so they move with the object.</summary>
    public bool HasErasures => _erasures.Count > 0;

    /// <summary>Origin of the element's local coordinate space in image coordinates.</summary>
    private Vector LocalOrigin
    {
        get
        {
            double left = Canvas.GetLeft(Element), top = Canvas.GetTop(Element);
            var v = new Vector(double.IsNaN(left) ? 0 : left, double.IsNaN(top) ? 0 : top);
            return UsesTranslate ? v + _offset : v;
        }
    }

    private Point ToLocal(Point imagePoint) => imagePoint - LocalOrigin;

    /// <summary>Convert an image-space eraser shape to this element's local space.</summary>
    public Geometry ToLocal(Geometry imageSpace)
    {
        var o = LocalOrigin;
        var g = imageSpace.Clone();
        g.Transform = new TranslateTransform(-o.X, -o.Y);
        g.Freeze();
        return g;
    }

    public void AddErasure(Geometry localGeometry)
    {
        _erasures.Add(localGeometry);
        UpdateClip();
    }

    public void RemoveErasure(Geometry localGeometry)
    {
        _erasures.Remove(localGeometry);
        UpdateClip();
    }

    /// <summary>Clip = everything minus the erased shapes (vector clip: crisp, no intermediate bitmaps).</summary>
    private void UpdateClip()
    {
        if (_erasures.Count == 0)
        {
            Element.Clip = null;
            return;
        }
        var erased = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var g in _erasures) erased.Children.Add(g);
        var everything = new RectangleGeometry(new Rect(-50000, -50000, 100000, 100000));
        var visible = new CombinedGeometry(GeometryCombineMode.Exclude, everything, erased);
        // an erasure that is still being drawn (unfrozen) keeps updating the clip by itself
        if (_erasures.TrueForAll(g => g.IsFrozen)) visible.Freeze();
        Element.Clip = visible;
    }

    /// <summary>Rebuild the clip once the erasures are final (frozen geometry renders faster).</summary>
    public void RefreshClip() => UpdateClip();

    /// <summary>The drag that created / reshaped this object ended (expensive work can run now).</summary>
    public virtual void OnDragFinished() { }

    /// <summary>Called before export: finish anything that is still a preview.</summary>
    public virtual void PrepareForExport() { }

    public virtual bool UsesStroke => true;
    public virtual bool UsesFill => false;
    public virtual bool UsesThickness => true;
    public virtual bool UsesFont => false;
    protected virtual bool UsesTranslate => true;
    protected virtual void OnOffsetChanged() { }

    /// <summary>
    /// The image was cropped: move this object by <paramref name="shift"/> so it stays on the same pixels,
    /// and use <paramref name="newImage"/> for anything that samples the screenshot (mosaic / blur).
    /// </summary>
    public virtual void Rebase(Vector shift, PixelImage newImage) => Offset += shift;

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

    // Points are appended to one PolyLineSegment (O(1) per point) instead of rebuilding the whole
    // path for every mouse move; the geometry is frozen when the stroke is finished.
    private readonly PolyLineSegment _segment = new() { IsStroked = true, IsSmoothJoin = true };
    private readonly PathGeometry _live;
    private Geometry _geometry;
    private Point _last;

    public bool IsHighlighter { get; }
    public override FrameworkElement Element => _path;

    public StrokeAnnotation(AnnotationStyle style, Point start, bool highlighter) : base(style)
    {
        IsHighlighter = highlighter;
        _last = start;
        _segment.Points.Add(start + new Vector(0.01, 0)); // a dot until the pointer moves
        var figure = new PathFigure { StartPoint = start, IsFilled = false, IsClosed = false };
        figure.Segments.Add(_segment);
        _live = new PathGeometry();
        _live.Figures.Add(figure);
        _geometry = _live;
        _path.Data = _live;
        Init();
    }

    private double Width => IsHighlighter ? Style.Thickness * 3 + 8 : Style.Thickness;

    public int PointCount => _segment.Points.Count + 1;

    public void AddPoint(Point p)
    {
        if ((p - _last).LengthSquared < 0.6) return;
        if (_geometry != _live) return; // already finished
        _last = p;
        _segment.Points.Add(p);
    }

    public override void OnDragFinished()
    {
        if (_geometry != _live) return;
        var frozen = _live.Clone();
        frozen.Freeze();
        _geometry = frozen;
        _path.Data = frozen;
    }

    protected override void ApplyStyle()
    {
        // highlighter: translucent color (cheaper than element opacity, which needs an offscreen layer)
        var c = Style.Stroke;
        if (IsHighlighter) c.A = (byte)Math.Round(c.A * 0.45);
        _path.Stroke = ColorUtil.Brush(c);
        _path.StrokeThickness = Width;
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

    protected override bool HitTestCore(Point p) => _geometry.StrokeContains(HitPen(Width), p - Offset);
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

    /// <summary>Move both endpoints (local coordinates, without the offset).</summary>
    public void SetPoints(Point start, Point end)
    {
        Start = start;
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

    protected override bool HitTestCore(Point p)
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

    protected override bool HitTestCore(Point p)
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

    /// <summary>The app font (embedded IBM Plex Sans Thai), so exported text matches the UI.</summary>
    public static FontFamily Font =>
        Application.Current?.TryFindResource("UiFont") as FontFamily ?? new FontFamily("Segoe UI");

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

    protected override bool HitTestCore(Point p) => (p - Offset - Center).Length <= Diameter / 2 + 3;
}

// ---------------------------------------------------------------- mosaic (redaction)

/// <summary>
/// Pixelates or blurs a region of the base image. The result is real pixels drawn over the image,
/// so the exported file never contains the original pixels of that region.
/// <para>
/// Pixelate: one pixel per block is computed and shown scaled up (nearest neighbor), so even a large
/// region costs only a small bitmap. Blur: while dragging, a GPU <see cref="BlurEffect"/> preview is
/// shown; the exact blur is baked when the drag ends (or before export).
/// </para>
/// </summary>
public sealed class MosaicAnnotation : Annotation
{
    private readonly Canvas _box = new() { ClipToBounds = true };
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private PixelImage _source;
    private PxRect _baked = default;
    private bool _previewing;
    private DispatcherTimer? _bakeTimer;

    public override void Rebase(Vector shift, PixelImage newImage)
    {
        _source = newImage;
        _baked = default;
        base.Rebase(shift, newImage); // Offset change regenerates from the new image
    }

    public Rect Rect { get; private set; }
    public override FrameworkElement Element => _box;
    public override bool UsesStroke => false;
    protected override bool UsesTranslate => false;

    public RedactMode Mode { get; }

    /// <summary>True while a drag is running: blur shows a cheap preview.</summary>
    public bool Live { get; set; }

    public MosaicAnnotation(AnnotationStyle style, Rect rect, PixelImage source, RedactMode mode = RedactMode.Pixelate) : base(style)
    {
        _source = source;
        Mode = mode;
        Rect = rect;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        _box.Children.Add(_image);
        Init();
    }

    public void SetRect(Rect r)
    {
        Rect = r;
        Regenerate();
    }

    private int BlockSize => (int)Math.Max(4, Math.Round(Style.Thickness * 2 + 4));
    private int BlurRadius => (int)Math.Max(2, Math.Round(Style.Thickness * 1.5 + 2));

    protected override void ApplyStyle()
    {
        _baked = default;
        Regenerate();
    }

    protected override void OnOffsetChanged() => Regenerate();

    public override void OnDragFinished()
    {
        Live = false;
        Bake();
    }

    public override void PrepareForExport() => Bake();

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

    private void Place(PxRect pr)
    {
        Canvas.SetLeft(_box, pr.X);
        Canvas.SetTop(_box, pr.Y);
        _box.Width = pr.Width;
        _box.Height = pr.Height;
    }

    private void Regenerate()
    {
        var pr = PixelRect;
        if (pr.IsEmpty)
        {
            _image.Source = null;
            _image.Effect = null;
            _baked = pr;
            _previewing = false;
            return;
        }
        if (pr == _baked && _image.Source != null && !_previewing) return;
        Place(pr);

        if (Mode == RedactMode.Pixelate)
        {
            Pixelate(pr);
            _baked = pr;
            _previewing = false;
            return;
        }

        // Blur: cheap preview now (GPU effect on the original pixels), exact pixels a moment later.
        ShowBlurPreview(pr);
        _bakeTimer ??= CreateBakeTimer();
        _bakeTimer.Stop();
        if (!Live) _bakeTimer.Start();
    }

    private DispatcherTimer CreateBakeTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        t.Tick += (_, _) => { t.Stop(); Bake(); };
        return t;
    }

    /// <summary>Compute the exact pixels now (blur only; pixelate is always exact).</summary>
    public void Bake()
    {
        _bakeTimer?.Stop();
        if (Mode != RedactMode.Blur) return;
        var pr = PixelRect;
        if (pr.IsEmpty || (pr == _baked && !_previewing && _image.Source != null)) return;
        Place(pr);
        var bmp = BitmapSource.Create(pr.Width, pr.Height, 96, 96, PixelFormats.Pbgra32, null, BlurRegion(pr), pr.Width * 4);
        bmp.Freeze();
        _image.Effect = null;
        _image.Source = bmp;
        Canvas.SetLeft(_image, 0);
        Canvas.SetTop(_image, 0);
        _image.Width = pr.Width;
        _image.Height = pr.Height;
        _baked = pr;
        _previewing = false;
    }

    private void ShowBlurPreview(PxRect pr)
    {
        int pad = BlurRadius * 3;
        var src = new PxRect(pr.X - pad, pr.Y - pad, pr.Width + 2 * pad, pr.Height + 2 * pad)
            .Intersect(new PxRect(0, 0, _source.Width, _source.Height));
        var crop = new CroppedBitmap(_source.Source, new Int32Rect(src.X, src.Y, src.Width, src.Height));
        crop.Freeze();
        _image.Source = crop;
        _image.Width = src.Width;
        _image.Height = src.Height;
        Canvas.SetLeft(_image, src.X - pr.X);
        Canvas.SetTop(_image, src.Y - pr.Y);
        // three box passes of radius r are close to a Gaussian with sigma ~ r; BlurEffect radius ~ 3 sigma
        _image.Effect ??= new BlurEffect { KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        ((BlurEffect)_image.Effect).Radius = BlurRadius * 2.2;
        _previewing = true;
        _baked = pr;
    }

    /// <summary>One averaged pixel per block, shown scaled up with nearest-neighbor sampling.</summary>
    private void Pixelate(PxRect pr)
    {
        int bs = BlockSize;
        int w = pr.Width, h = pr.Height;
        int cols = (w + bs - 1) / bs, rows = (h + bs - 1) / bs;
        var dst = new byte[cols * rows * 4];
        byte[] src = _source.Pixels;
        int sstride = _source.Stride;
        for (int by = 0, row = 0; by < h; by += bs, row++)
        {
            int bh = Math.Min(bs, h - by);
            for (int bx = 0, col = 0; bx < w; bx += bs, col++)
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
                int di = (row * cols + col) * 4;
                dst[di] = (byte)(sb / n);
                dst[di + 1] = (byte)(sg / n);
                dst[di + 2] = (byte)(sr / n);
                dst[di + 3] = 255;
            }
        }
        var bmp = BitmapSource.Create(cols, rows, 96, 96, PixelFormats.Pbgra32, null, dst, cols * 4);
        bmp.Freeze();
        _image.Effect = null;
        _image.Source = bmp;
        _image.Width = cols * bs; // partial blocks at the right / bottom are cut by the box
        _image.Height = rows * bs;
        Canvas.SetLeft(_image, 0);
        Canvas.SetTop(_image, 0);
    }

    /// <summary>
    /// Strong Gaussian-like blur (3 box-blur passes) of the region; pixels around the region are
    /// sampled too so edges blend. The result replaces the original pixels in the export.
    /// </summary>
    private byte[] BlurRegion(PxRect pr)
    {
        int radius = BlurRadius;
        int pad = radius * 3;
        var src = new PxRect(pr.X - pad, pr.Y - pad, pr.Width + 2 * pad, pr.Height + 2 * pad)
            .Intersect(new PxRect(0, 0, _source.Width, _source.Height));
        int w = src.Width, h = src.Height, n = w * h * 3;
        var pool = ArrayPool<float>.Shared;
        float[] a = pool.Rent(n), b = pool.Rent(n), sums = pool.Rent(w * 3);
        try
        {
            byte[] px = _source.Pixels;
            for (int y = 0; y < h; y++)
            {
                int si = (src.Y + y) * _source.Stride + src.X * 4;
                int di = y * w * 3;
                for (int x = 0; x < w; x++, si += 4, di += 3)
                {
                    a[di] = px[si];
                    a[di + 1] = px[si + 1];
                    a[di + 2] = px[si + 2];
                }
            }
            for (int pass = 0; pass < 3; pass++)
            {
                BoxH(a, b, w, h, radius);
                BoxV(b, a, w, h, radius, sums);
            }
            var dst = new byte[pr.Width * pr.Height * 4];
            int ox = pr.X - src.X, oy = pr.Y - src.Y;
            for (int y = 0; y < pr.Height; y++)
            {
                int si = ((y + oy) * w + ox) * 3;
                int di = y * pr.Width * 4;
                for (int x = 0; x < pr.Width; x++, si += 3, di += 4)
                {
                    dst[di] = (byte)Math.Clamp(a[si], 0, 255);
                    dst[di + 1] = (byte)Math.Clamp(a[si + 1], 0, 255);
                    dst[di + 2] = (byte)Math.Clamp(a[si + 2], 0, 255);
                    dst[di + 3] = 255;
                }
            }
            return dst;
        }
        finally
        {
            pool.Return(a);
            pool.Return(b);
            pool.Return(sums);
        }
    }

    private static void BoxH(float[] src, float[] dst, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int c = 0; c < 3; c++)
            {
                float sum = 0;
                for (int k = -r; k <= r; k++) sum += src[(row + Math.Clamp(k, 0, w - 1)) * 3 + c];
                for (int x = 0; x < w; x++)
                {
                    dst[(row + x) * 3 + c] = sum * inv;
                    int add = Math.Min(x + r + 1, w - 1), sub = Math.Max(x - r, 0);
                    sum += src[(row + add) * 3 + c] - src[(row + sub) * 3 + c];
                }
            }
        }
    }

    /// <summary>Vertical box pass, row by row with running column sums (sequential memory access).</summary>
    private static void BoxV(float[] src, float[] dst, int w, int h, int r, float[] sums)
    {
        float inv = 1f / (2 * r + 1);
        int rowLen = w * 3;
        Array.Clear(sums, 0, rowLen);
        for (int k = -r; k <= r; k++)
        {
            int ry = Math.Clamp(k, 0, h - 1) * rowLen;
            for (int i = 0; i < rowLen; i++) sums[i] += src[ry + i];
        }
        for (int y = 0; y < h; y++)
        {
            int dy = y * rowLen;
            int add = Math.Min(y + r + 1, h - 1) * rowLen, sub = Math.Max(y - r, 0) * rowLen;
            for (int i = 0; i < rowLen; i++)
            {
                dst[dy + i] = sums[i] * inv;
                sums[i] += src[add + i] - src[sub + i];
            }
        }
    }

    public override Rect LocalBounds => Rect;
}

// ---------------------------------------------------------------- spotlight

/// <summary>
/// An area that stays bright while the rest of the image is dimmed. All spotlights are combined by the
/// editor into ONE dim layer (so two spotlights don't darken each other); this element only shows a
/// faint outline so the area can be selected, moved and deleted.
/// </summary>
public sealed class SpotlightAnnotation : Annotation
{
    private readonly Path _path = new() { Fill = Brushes.Transparent, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 } };

    public bool IsEllipse { get; }
    public Rect Rect { get; private set; }
    public override FrameworkElement Element => _path;
    public override bool UsesStroke => false;

    public SpotlightAnnotation(AnnotationStyle style, Rect rect, bool ellipse) : base(style)
    {
        IsEllipse = ellipse;
        Rect = rect;
        Init();
    }

    public void SetRect(Rect r)
    {
        Rect = r;
        ApplyStyle();
    }

    /// <summary>The bright area in image coordinates (offset included).</summary>
    public Geometry HoleGeometry
    {
        get
        {
            var r = Rect;
            r.Offset(Offset);
            double rad = Math.Min(Style.Thickness * 1.5, Math.Min(r.Width, r.Height) / 2);
            return IsEllipse ? new EllipseGeometry(r) : new RectangleGeometry(r, rad, rad);
        }
    }

    private static readonly Brush OutlineBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)));

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    protected override void ApplyStyle()
    {
        double rad = Math.Min(Style.Thickness * 1.5, Math.Min(Rect.Width, Rect.Height) / 2);
        Geometry g = IsEllipse ? new EllipseGeometry(Rect) : new RectangleGeometry(Rect, rad, rad);
        g.Freeze();
        _path.Data = g;
        _path.Stroke = OutlineBrush;
    }

    public override Rect LocalBounds => Rect;
}
