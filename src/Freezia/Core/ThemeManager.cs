using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Freezia.UI;

namespace Freezia.Core;

public enum AppTheme
{
    Dark,
    Light,
    System,
}

/// <summary>
/// Swaps the color dictionary (Themes/Colors.Dark.xaml or Colors.Light.xaml) at runtime.
/// All XAML uses DynamicResource for brushes, so open windows update immediately.
/// </summary>
public static class ThemeManager
{
    private const string DarkUri = "pack://application:,,,/Freezia;component/Themes/Colors.Dark.xaml";
    private const string LightUri = "pack://application:,,,/Freezia;component/Themes/Colors.Light.xaml";

    private static AppTheme _mode = AppTheme.Dark;
    private static bool _listening;

    public static bool IsDark { get; private set; } = true;
    public static AppTheme Mode => _mode;

    public static event Action? Changed;

    // ---------------------------------------------------------------- accent color

    /// <summary>Accent override (null = the palette's own blue).</summary>
    private static Color? _accent;
    private const string AccentMarkerKey = "__FreeziaAccentOverrides";

    public static Color? Accent => _accent;

    /// <summary>Built-in accent palettes. Null = default blue of the current light/dark palette.</summary>
    public static readonly (string Th, string En, Color? Color)[] AccentPresets =
    {
        ("ฟ้า (ค่าเริ่มต้น)", "Blue (default)", null),
        ("คราม", "Indigo", Color.FromRgb(0x63, 0x66, 0xF1)),
        ("ม่วง", "Purple", Color.FromRgb(0x8B, 0x5C, 0xF6)),
        ("ชมพู", "Pink", Color.FromRgb(0xEC, 0x48, 0x99)),
        ("แดง", "Red", Color.FromRgb(0xEF, 0x44, 0x44)),
        ("ส้ม", "Orange", Color.FromRgb(0xF9, 0x73, 0x16)),
        ("เหลืองอำพัน", "Amber", Color.FromRgb(0xF5, 0x9E, 0x0B)),
        ("เขียว", "Green", Color.FromRgb(0x22, 0xC5, 0x5E)),
        ("เขียวหัวเป็ด", "Teal", Color.FromRgb(0x14, 0xB8, 0xA6)),
        ("ฟ้าน้ำทะเล", "Cyan", Color.FromRgb(0x06, 0xB6, 0xD4)),
        ("เทาเข้ม", "Graphite", Color.FromRgb(0x64, 0x74, 0x8B)),
    };

    /// <summary>Use a custom accent color (alpha is ignored). Null restores the default blue.</summary>
    public static void SetAccent(Color? accent)
    {
        _accent = accent.HasValue ? Color.FromRgb(accent.Value.R, accent.Value.G, accent.Value.B) : null;
        ApplyAccentOverrides();
        Changed?.Invoke();
    }

    /// <summary>
    /// Accent brushes are overridden by a small dictionary merged after the palette, so they survive
    /// light/dark switches. Hover / pressed / soft / text colors are derived from the chosen color.
    /// </summary>
    private static void ApplyAccentOverrides()
    {
        var app = Application.Current;
        if (app == null) return;
        var dicts = app.Resources.MergedDictionaries;
        foreach (var old in dicts.Where(d => d.Contains(AccentMarkerKey)).ToList()) dicts.Remove(old);
        if (_accent is not Color c) return;

        var rd = new ResourceDictionary { [AccentMarkerKey] = true };
        rd["AccentBrush"] = Frozen(c);
        rd["AccentHoverBrush"] = Frozen(Mix(c, Colors.White, IsDark ? 0.18 : 0.14));
        rd["AccentPressedBrush"] = Frozen(Mix(c, Colors.Black, 0.15));
        rd["AccentSoftBrush"] = Frozen(Color.FromArgb(IsDark ? (byte)0x33 : (byte)0x26, c.R, c.G, c.B));
        // dark text on bright accents (amber, green, cyan…), white text otherwise
        rd["AccentForegroundBrush"] = Frozen(Luminance(c) > 0.36 ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White);
        dicts.Add(rd);
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>Relative luminance (WCAG), 0..1.</summary>
    public static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static void Apply(AppTheme mode)
    {
        _mode = mode;
        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _listening = true;
        }
        Refresh();
    }

    public static void Shutdown()
    {
        if (_listening) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _listening = false;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode != AppTheme.System) return;
        Application.Current?.Dispatcher.BeginInvoke(Refresh);
    }

    private static void Refresh()
    {
        bool dark = _mode switch
        {
            AppTheme.Light => false,
            AppTheme.System => SystemUsesDarkTheme(),
            _ => true,
        };
        var app = Application.Current;
        if (app == null) return;
        var dicts = app.Resources.MergedDictionaries;
        // App.xaml may reference the palette with a relative URI ("Themes/Colors.Dark.xaml"),
        // so match on the file name. Later merged dictionaries win, so the old palette must be removed,
        // not just shadowed by inserting a new one in front of it.
        var palettes = dicts.Where(IsPalette).ToList();
        bool showsDark = palettes.Count == 1 && palettes[0].Source!.OriginalString.Contains("Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase);
        bool showsLight = palettes.Count == 1 && palettes[0].Source!.OriginalString.Contains("Colors.Light.xaml", StringComparison.OrdinalIgnoreCase);
        IsDark = dark;
        if ((dark && showsDark) || (!dark && showsLight)) return;

        foreach (var d in palettes) dicts.Remove(d);
        dicts.Insert(0, new ResourceDictionary { Source = new Uri(dark ? DarkUri : LightUri, UriKind.Absolute) });
        ApplyAccentOverrides(); // hover/soft shades depend on light vs dark

        foreach (Window w in app.Windows) WindowHelper.ApplyChrome(w);
        Changed?.Invoke();
    }

    private static bool IsPalette(ResourceDictionary d)
    {
        string? src = d.Source?.OriginalString;
        return src != null && (src.EndsWith("Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase)
                            || src.EndsWith("Colors.Light.xaml", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Windows "Choose your app mode" (Personalize › AppsUseLightTheme = 0 means dark).</summary>
    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }
}
