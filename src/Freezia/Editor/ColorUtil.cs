using System;
using System.Globalization;
using System.Windows.Media;

namespace Freezia.Editor;

public static class ColorUtil
{
    /// <summary>HSV (h 0..360, s 0..1, v 0..1) to RGB.</summary>
    public static Color FromHsv(double h, double s, double v, byte a = 255)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromArgb(a, ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    public static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        double s = max <= 0 ? 0 : d / max;
        return (h, s, max);
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);

    /// <summary>"#RRGGBB" (alpha ignored).</summary>
    public static string ToHex6(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>"#AARRGGBB".</summary>
    public static string ToHex8(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Parses #RGB, #RRGGBB or #AARRGGBB (with or without '#').</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim().TrimStart('#');
        if (s.Length == 3) s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8) return false;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return false;
        color = Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Color Parse(string? text, Color fallback) => TryParse(text, out var c) ? c : fallback;

    /// <summary>Black or white, whichever reads better on <paramref name="bg"/>.</summary>
    public static Color Contrast(Color bg)
    {
        double l = 0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B;
        return l > 150 ? Colors.Black : Colors.White;
    }

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
