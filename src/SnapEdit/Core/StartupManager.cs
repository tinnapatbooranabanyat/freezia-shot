using System;
using Microsoft.Win32;

namespace SnapEdit.Core;

/// <summary>"Start with Windows" via HKCU\...\Run (same value the installer writes).</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = AppInfo.Name;

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
            }
            catch
            {
                return false;
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled)
            {
                string exe = Environment.ProcessPath ?? throw new InvalidOperationException("No process path");
                key.SetValue(ValueName, $"\"{exe}\" --autostart", RegistryValueKind.String);
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to change start-with-Windows", ex);
            throw;
        }
    }
}
