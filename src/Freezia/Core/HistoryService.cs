using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Freezia.Core;

/// <summary>
/// Keeps the latest captures as PNG files in %LocalAppData%\Freezia\history (local only, pruned to the
/// configured limit, removed on uninstall). File names are timestamps so the folder sorts by time.
/// </summary>
public static class HistoryService
{
    public sealed record Entry(string Path, DateTime Time);

    private static readonly object Gate = new();
    private static string? _lastHash;

    public static event Action? Changed;

    public static string Dir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "history");

    /// <summary>Remember an image (skipped when history is off or it equals the previous entry).</summary>
    public static void Add(BitmapSource image, AppSettings settings)
    {
        if (!settings.HistoryEnabled || settings.HistoryLimit <= 0) return;
        if (!image.IsFrozen) image = image.Clone();
        if (!image.IsFrozen) image.Freeze();
        int limit = settings.HistoryLimit;
        var time = DateTime.Now;
        Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream();
                ImageSaver.Encode(image, ImageFormatKind.Png, 100, ms);
                byte[] png = ms.ToArray();
                string hash = Convert.ToHexString(SHA1.HashData(png));
                lock (Gate)
                {
                    if (hash == _lastHash) return; // same image copied / saved twice
                    _lastHash = hash;
                    Directory.CreateDirectory(Dir);
                    string name = time.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".png";
                    File.WriteAllBytes(System.IO.Path.Combine(Dir, name), png);
                    Prune(limit);
                }
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
            }
            catch (Exception ex)
            {
                Logger.Error("History save failed", ex);
            }
        });
    }

    /// <summary>Newest first.</summary>
    public static List<Entry> List()
    {
        try
        {
            if (!Directory.Exists(Dir)) return new List<Entry>();
            return Directory.GetFiles(Dir, "*.png")
                .Select(p => new Entry(p, ParseTime(p)))
                .OrderByDescending(e => e.Time)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Error("History list failed", ex);
            return new List<Entry>();
        }
    }

    private static DateTime ParseTime(string path)
    {
        string n = System.IO.Path.GetFileNameWithoutExtension(path);
        return DateTime.TryParseExact(n, "yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : File.GetLastWriteTime(path);
    }

    public static void Prune(int limit)
    {
        lock (Gate)
        {
            foreach (var e in List().Skip(Math.Max(0, limit)))
            {
                try { File.Delete(e.Path); } catch { /* in use: next time */ }
            }
        }
    }

    public static void Delete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) { Logger.Error("History delete failed", ex); }
        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Gate)
        {
            foreach (var e in List())
            {
                try { File.Delete(e.Path); } catch { }
            }
            _lastHash = null;
        }
        Changed?.Invoke();
    }

    /// <summary>Load full-size, file not kept open.</summary>
    public static BitmapSource Load(string path)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        b.UriSource = new Uri(path);
        b.EndInit();
        b.Freeze();
        return b;
    }

    /// <summary>Small decoded thumbnail for the history grid.</summary>
    public static BitmapSource Thumbnail(string path, int width)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.DecodePixelWidth = width;
        b.UriSource = new Uri(path);
        b.EndInit();
        b.Freeze();
        return b;
    }
}
