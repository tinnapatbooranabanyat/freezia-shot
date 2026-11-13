using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SnapEdit.Core;

/// <summary>Tiny file logger: %LocalAppData%\SnapEdit\logs\snapedit-yyyyMMdd.log (local only).</summary>
public static class Logger
{
    private static readonly object Gate = new();

    public static string LogDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "logs");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                string file = Path.Combine(LogDir, "snapedit-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(" [").Append(level).Append("] ").AppendLine(message);
                if (ex != null) sb.AppendLine(ex.ToString());
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
                CleanupOldLogs();
            }
        }
        catch
        {
            // logging must never crash the app
        }
    }

    private static DateTime _lastCleanup = DateTime.MinValue;

    private static void CleanupOldLogs()
    {
        if ((DateTime.Now - _lastCleanup).TotalHours < 12) return;
        _lastCleanup = DateTime.Now;
        foreach (var f in Directory.GetFiles(LogDir, "snapedit-*.log"))
        {
            if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14))
                File.Delete(f);
        }
    }
}
