using System;
using System.Linq;
using System.Windows;
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
