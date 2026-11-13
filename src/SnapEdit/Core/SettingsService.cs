using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnapEdit.Core;

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON with atomic writes.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);

    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? Changed;

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            else
            {
                Current = new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load settings, using defaults", ex);
            Current = new AppSettings();
        }
        Normalize(Current);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            string tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(tmp, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save settings", ex);
        }
    }

    /// <summary>Replace the settings, persist them and notify listeners.</summary>
    public void Update(AppSettings settings)
    {
        Normalize(settings);
        Current = settings;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Normalize(AppSettings s)
    {
        s.HotkeyRegion ??= Hotkey.None;
        s.HotkeyScreen ??= Hotkey.None;
        s.HotkeyAllScreens ??= Hotkey.None;
        s.Editor ??= new EditorSettings();
        s.Editor.RecentColors ??= new();
        if (s.Editor.RecentColors.Count > 8)
            s.Editor.RecentColors.RemoveRange(8, s.Editor.RecentColors.Count - 8);
        s.Editor.Thickness = Math.Clamp(s.Editor.Thickness, 1, 40);
        s.Editor.FontSize = Math.Clamp(s.Editor.FontSize, 8, 160);
        s.JpegQuality = Math.Clamp(s.JpegQuality, 30, 100);
        if (!Enum.IsDefined(s.Format)) s.Format = ImageFormatKind.Png;
        if (!Enum.IsDefined(s.AfterFullCapture)) s.AfterFullCapture = AfterCaptureAction.OpenEditor;
    }

    /// <summary>Deep copy via JSON round-trip (used by the settings window for editing).</summary>
    public static AppSettings Clone(AppSettings s) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, JsonOptions), JsonOptions)!;
}
