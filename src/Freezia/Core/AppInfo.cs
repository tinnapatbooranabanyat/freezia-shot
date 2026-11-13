using System.Reflection;

namespace Freezia.Core;

public static class AppInfo
{
    public const string Name = "Freezia";

    /// <summary>Single-instance mutex name. Must match AppMutex in installer/Freezia.iss.</summary>
    public const string MutexName = "Freezia-6F1C2B7A-3D4E-4C2B-9A51-8E2D7B1F0C63";

    /// <summary>Named event used by a second instance to talk to the running one.</summary>
    public const string ActivateEventName = MutexName + "-activate";
    public const string ExitEventName = MutexName + "-exit";
    public const string OpenEventName = MutexName + "-open";

    /// <summary>Inno Setup AppId (installer/Freezia.iss); its uninstall key is "{AppId}_is1".</summary>
    public const string InstallerAppId = "{8E3F1A6C-2B9D-4E57-A1C4-7D0B3F9E2A58}";

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
}
