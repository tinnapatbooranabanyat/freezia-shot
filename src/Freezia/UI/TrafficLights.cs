using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Freezia.UI;

/// <summary>
/// macOS-style red / yellow / green buttons on a dark translucent capsule. Pointing at the group shows
/// × − + inside the dots. A button with no action is drawn gray and does nothing (like macOS panels).
/// Clicks are handled here (no drag, no double-click) and run on release over the dot.
/// </summary>
public sealed class TrafficLights : Border
{
    private readonly Path[] _glyphs = new Path[3];
    private readonly Grid[] _dots = new Grid[3];

    public TrafficLights(Action? red, Action? yellow, Action? green, Func<string>? redTip, Func<string>? yellowTip, Func<string>? greenTip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var defs = new (string Fill, string Edge, string Glyph, Action? Act, Func<string>? Tip)[]
        {
            ("#FF5F57", "#E0443E", "M3,3 L9,9 M9,3 L3,9", red, redTip),
            ("#FEBC2E", "#DEA123", "M2.5,6 H9.5", yellow, yellowTip),
            ("#28C840", "#1AAB29", "M6,2.5 V9.5 M2.5,6 H9.5", green, greenTip),
        };
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            bool enabled = d.Act != null;
            var glyph = new Path
            {
                Data = Geometry.Parse(d.Glyph),
                Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0x40, 0x10, 0x00)),
                StrokeThickness = 1.3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 12,
                Height = 12,
                Opacity = 0,
                IsHitTestVisible = false,
            };
            _glyphs[i] = glyph;
            var dot = new Grid
            {
                Width = 12,
                Height = 12,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
                Background = Brushes.Transparent,
            };
            dot.Children.Add(new Ellipse
            {
                Fill = enabled ? (Brush)new BrushConverter().ConvertFromString(d.Fill)! : new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x70)),
                Stroke = enabled ? (Brush)new BrushConverter().ConvertFromString(d.Edge)! : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5A)),
                StrokeThickness = 0.8,
            });
            if (enabled) dot.Children.Add(glyph);
            _dots[i] = dot;
            var act = d.Act;
            if (d.Tip is { } tip)
            {
                dot.ToolTip = tip();
                dot.ToolTipOpening += (_, _) => dot.ToolTip = tip();
            }
            dot.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (act != null) dot.CaptureMouse();
                e.Handled = true;
            };
            dot.PreviewMouseLeftButtonUp += (_, e) =>
            {
                bool over = dot.IsMouseOver;
                if (dot.IsMouseCaptured) dot.ReleaseMouseCapture();
                e.Handled = true;
                if (over && act != null) Dispatcher.BeginInvoke(act);
            };
            dot.MouseLeftButtonDown += (_, e) => e.Handled = true;
            dot.MouseLeftButtonUp += (_, e) => e.Handled = true;
            row.Children.Add(dot);
        }
        Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x10, 0x10, 0x14));
        CornerRadius = new CornerRadius(9);
        Padding = new Thickness(6, 4, 6, 4);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Child = row;
        MouseEnter += (_, _) => SetGlyphsVisible(true);
        MouseLeave += (_, _) => SetGlyphsVisible(false);
    }

    /// <summary>Show × − + inside the dots (pointing at the group).</summary>
    public void SetGlyphsVisible(bool on)
    {
        foreach (var g in _glyphs) g.Opacity = on ? 1 : 0;
    }

    /// <summary>Change a button's tooltip text (e.g. after a toggle).</summary>
    public void SetTip(int index, string text) => _dots[index].ToolTip = text;
}
