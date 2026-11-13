using System.Collections.Generic;

namespace Freezia.Core;

public enum ImageFormatKind
{
    Png,
    Jpg,
}

public enum AfterCaptureAction
{
    OpenEditor,
    Copy,
    Save,
}

/// <summary>Everything stored in %AppData%\Freezia\settings.json.</summary>
public sealed class AppSettings
{
    public Hotkey HotkeyRegion { get; set; } = new(HotkeyModifiers.None, Hotkey.VK_SNAPSHOT);
    public Hotkey HotkeyScreen { get; set; } = new(HotkeyModifiers.Ctrl, Hotkey.VK_SNAPSHOT);
    public Hotkey HotkeyAllScreens { get; set; } = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, Hotkey.VK_SNAPSHOT);

    /// <summary>Custom save folder; null/empty = the user's Downloads known folder.</summary>
    public string? SaveFolder { get; set; }

    public ImageFormatKind Format { get; set; } = ImageFormatKind.Png;
    public int JpegQuality { get; set; } = 92;

    public AfterCaptureAction AfterFullCapture { get; set; } = AfterCaptureAction.OpenEditor;

    public bool StartWithWindows { get; set; }

    public AppLanguage Language { get; set; } = AppLanguage.Thai;
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public EditorSettings Editor { get; set; } = new();
}

public sealed class EditorSettings
{
    public string StrokeColor { get; set; } = "#FFFF3B30";
    public string FillColor { get; set; } = "#66FFD60A";
    public bool FillEnabled { get; set; }
    public double Thickness { get; set; } = 4;
    public double FontSize { get; set; } = 24;
    public List<string> RecentColors { get; set; } = new();
    /// <summary>Colors the user saved with the "+" button ("#AARRGGBB").</summary>
    public List<string> SavedColors { get; set; } = new();
}
