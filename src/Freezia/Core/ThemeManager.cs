using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Freezia.UI;

namespace Freezia.Core;

/// <summary>Legacy light/dark setting (kept so old settings.json files still load).</summary>
public enum AppTheme
{
    Dark,
    Light,
    System,
}

/// <summary>A complete color theme: backgrounds, surfaces, borders, text and a default accent.</summary>
public sealed record ThemeDef(
    string Id, string Th, string En, bool IsDark,
    Color Bg, Color Surface, Color Surface2, Color Surface3, Color Border,
    Color Text, Color TextDim, Color Accent);

/// <summary>
/// Applies a full color theme (like an editor color theme) plus an optional accent override.
/// Palettes are built in code into a merged ResourceDictionary; every XAML brush is a
/// DynamicResource, so open windows repaint immediately.
/// </summary>
public static class ThemeManager
{
    public const string SystemId = "system";
    public const string CustomId = "custom";

    private static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>Built-in themes (original palettes).</summary>
    public static readonly ThemeDef[] Themes =
    {
        new("dark", "มืด", "Dark", true, C(0x17181C), C(0x202127), C(0x2A2B33), C(0x34353E), C(0x3B3C46), C(0xEDEDF2), C(0x9C9DAA), C(0x5B8CFF)),
        new("light", "สว่าง", "Light", false, C(0xF2F3F7), C(0xFFFFFF), C(0xF1F2F6), C(0xE5E7EE), C(0xD8DBE3), C(0x1B1C22), C(0x676A77), C(0x3D6FEF)),
        new("midnight", "เที่ยงคืน", "Midnight", true, C(0x0F1626), C(0x151E31), C(0x1C2740), C(0x24304D), C(0x2C3A5A), C(0xE6ECF8), C(0x8C9BB8), C(0x5B8CFF)),
        new("oled", "ดำสนิท", "Pure black", true, C(0x000000), C(0x0B0B0D), C(0x151518), C(0x1F1F23), C(0x2A2A2F), C(0xF2F2F5), C(0x8E8E96), C(0x5B8CFF)),
        new("forest", "ป่าสน", "Forest", true, C(0x121A16), C(0x18221D), C(0x1F2C25), C(0x27362E), C(0x304236), C(0xE6F0EA), C(0x8FA79A), C(0x3FBF7F)),
        new("mocha", "มอคค่า", "Mocha", true, C(0x1E1915), C(0x26201B), C(0x2F2822), C(0x39302A), C(0x443A32), C(0xF1E8E0), C(0xA8998B), C(0xE59A5C)),
        new("grape", "องุ่น", "Grape", true, C(0x1A1625), C(0x221D30), C(0x2A243B), C(0x332C47), C(0x3D3554), C(0xECE8F7), C(0x9D94B8), C(0xA78BFA)),
        new("paper", "กระดาษ", "Paper", false, C(0xF4F1EA), C(0xFBF9F4), C(0xEFEBE2), C(0xE5E0D5), C(0xD8D1C3), C(0x2A2620), C(0x7A7266), C(0xC2672E)),
        new("sakura", "ซากุระ", "Sakura", false, C(0xFBF1F4), C(0xFFFFFF), C(0xF8E8EE), C(0xF1DAE3), C(0xEACBD7), C(0x2B1E24), C(0x8A6F7B), C(0xE0457B)),
        new("sky", "ฟ้าใส", "Clear sky", false, C(0xEEF4FB), C(0xFFFFFF), C(0xE6EEF8), C(0xD9E4F2), C(0xC9D7EA), C(0x18222F), C(0x63738A), C(0x2F7BEA)),
    };

    private const string PaletteMarkerKey = "__FreeziaPalette";
    private const string AccentMarkerKey = "__FreeziaAccentOverrides";

    private static string _themeId = "dark";
    private static Color? _customBackground;
    private static Color? _accent;
    private static bool _listening;

    /// <summary>The theme currently on screen (resolved: "system" and "custom" become real palettes).</summary>
    public static ThemeDef Current { get; private set; } = Themes[0];
    public static bool IsDark => Current.IsDark;

    /// <summary>The selected theme id ("system", "custom" or a built-in id).</summary>
    public static string ThemeId => _themeId;
    public static Color? CustomBackground => _customBackground;
    public static Color? Accent => _accent;

    public static event Action? Changed;

    public static ThemeDef? Find(string? id) => Themes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Select a theme. <paramref name="customBackground"/> is used when id = "custom".</summary>
    public static void Apply(string? themeId, Color? customBackground = null)
    {
        _themeId = string.IsNullOrWhiteSpace(themeId) ? "dark" : themeId;
        if (customBackground.HasValue) _customBackground = customBackground;
        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _listening = true;
        }
        Refresh();
    }

    /// <summary>Use a custom accent color (alpha is ignored). Null = the theme's own accent.</summary>
    public static void SetAccent(Color? accent)
    {
        _accent = accent.HasValue ? Color.FromRgb(accent.Value.R, accent.Value.G, accent.Value.B) : null;
        ApplyAccent();
        Changed?.Invoke();
    }

    public static void Shutdown()
    {
        if (_listening) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _listening = false;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_themeId != SystemId) return;
        Application.Current?.Dispatcher.BeginInvoke(Refresh);
    }

    /// <summary>Resolve an id to a palette (system → dark/light from Windows, custom → generated).</summary>
    public static ThemeDef Resolve(string id, Color? customBackground)
    {
        if (id == SystemId) return SystemUsesDarkTheme() ? Themes[0] : Themes[1];
        if (id == CustomId) return FromBackground(customBackground ?? C(0x1E1E2A));
        return Find(id) ?? Themes[0];
    }

    private static void Refresh()
    {
        var app = Application.Current;
        if (app == null) return;
        Current = Resolve(_themeId, _customBackground);

        var dicts = app.Resources.MergedDictionaries;
        // Remove the XAML palette from App.xaml and any palette we built before. Later merged
        // dictionaries win, so the old palette must be removed, not shadowed.
        foreach (var d in dicts.Where(IsPalette).ToList()) dicts.Remove(d);
        dicts.Insert(0, BuildPalette(Current));
        ApplyAccent();

        foreach (Window w in app.Windows) WindowHelper.ApplyChrome(w);
        Changed?.Invoke();
    }

    private static bool IsPalette(ResourceDictionary d)
    {
        if (d.Contains(PaletteMarkerKey)) return true;
        string? src = d.Source?.OriginalString;
        return src != null && (src.EndsWith("Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase)
                            || src.EndsWith("Colors.Light.xaml", StringComparison.OrdinalIgnoreCase));
    }

    private static ResourceDictionary BuildPalette(ThemeDef t)
    {
        bool dark = t.IsDark;
        return new ResourceDictionary
        {
            [PaletteMarkerKey] = t.Id,
            ["BgBrush"] = Frozen(t.Bg),
            ["SurfaceBrush"] = Frozen(t.Surface),
            ["Surface2Brush"] = Frozen(t.Surface2),
            ["Surface3Brush"] = Frozen(t.Surface3),
            ["BorderBrush"] = Frozen(t.Border),
            ["TextBrush"] = Frozen(t.Text),
            ["TextDimBrush"] = Frozen(t.TextDim),
            ["DangerBrush"] = Frozen(dark ? C(0xFF5C5C) : C(0xE5484D)),
            ["HoverBrush"] = Frozen(dark ? Color.FromArgb(0x14, 255, 255, 255) : Color.FromArgb(0x0F, 0, 0, 0)),
            ["PressedBrush"] = Frozen(dark ? Color.FromArgb(0x22, 255, 255, 255) : Color.FromArgb(0x1A, 0, 0, 0)),
            ["SwatchRingBrush"] = Frozen(dark ? Color.FromArgb(0x26, 255, 255, 255) : Color.FromArgb(0x26, 0, 0, 0)),
        };
    }

    /// <summary>
    /// Accent brushes live in their own dictionary merged last. Hover / pressed / soft / text colors
    /// are derived from the accent (the user's override, or the theme's default accent).
    /// </summary>
    private static void ApplyAccent()
    {
        var app = Application.Current;
        if (app == null) return;
        var dicts = app.Resources.MergedDictionaries;
        foreach (var old in dicts.Where(d => d.Contains(AccentMarkerKey)).ToList()) dicts.Remove(old);

        var c = _accent ?? Current.Accent;
        bool dark = Current.IsDark;
        dicts.Add(new ResourceDictionary
        {
            [AccentMarkerKey] = true,
            ["AccentBrush"] = Frozen(c),
            ["AccentHoverBrush"] = Frozen(Mix(c, Colors.White, dark ? 0.18 : 0.14)),
            ["AccentPressedBrush"] = Frozen(Mix(c, Colors.Black, 0.15)),
            ["AccentSoftBrush"] = Frozen(Color.FromArgb(dark ? (byte)0x33 : (byte)0x26, c.R, c.G, c.B)),
            // dark text on bright accents (amber, green, cyan…), white text otherwise
            ["AccentForegroundBrush"] = Frozen(Luminance(c) > 0.36 ? C(0x141418) : Colors.White),
        });
    }

    /// <summary>
    /// Generate a whole theme from one background color: surfaces step away from the background,
    /// text and borders are picked for contrast, and the accent stays blue unless overridden.
    /// </summary>
    public static ThemeDef FromBackground(Color bg)
    {
        bg = Color.FromRgb(bg.R, bg.G, bg.B);
        bool dark = Luminance(bg) < 0.22;
        if (dark)
        {
            return new ThemeDef(CustomId, "กำหนดเอง", "Custom", true,
                bg,
                Mix(bg, Colors.White, 0.04),
                Mix(bg, Colors.White, 0.08),
                Mix(bg, Colors.White, 0.13),
                Mix(bg, Colors.White, 0.17),
                Mix(Colors.White, bg, 0.07),
                Mix(Colors.White, bg, 0.42),
                C(0x5B8CFF));
        }
        return new ThemeDef(CustomId, "กำหนดเอง", "Custom", false,
            bg,
            Mix(bg, Colors.White, 0.65),
            Mix(bg, Colors.Black, 0.03),
            Mix(bg, Colors.Black, 0.08),
            Mix(bg, Colors.Black, 0.14),
            Mix(bg, Colors.Black, 0.88),
            Mix(bg, Colors.Black, 0.58),
            C(0x3D6FEF));
    }

    /// <summary>Built-in accent palettes. Null = the current theme's own accent.</summary>
    public static readonly (string Th, string En, Color? Color)[] AccentPresets =
    {
        ("ตามธีม (ค่าเริ่มต้น)", "Theme default", null),
        ("คราม", "Indigo", C(0x6366F1)),
        ("ม่วง", "Purple", C(0x8B5CF6)),
        ("ชมพู", "Pink", C(0xEC4899)),
        ("แดง", "Red", C(0xEF4444)),
        ("ส้ม", "Orange", C(0xF97316)),
        ("เหลืองอำพัน", "Amber", C(0xF59E0B)),
        ("เขียว", "Green", C(0x22C55E)),
        ("เขียวหัวเป็ด", "Teal", C(0x14B8A6)),
        ("ฟ้าน้ำทะเล", "Cyan", C(0x06B6D4)),
        ("เทาเข้ม", "Graphite", C(0x64748B)),
    };

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
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
