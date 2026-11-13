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

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
}
