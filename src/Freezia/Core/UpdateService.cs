using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Freezia.Core;

/// <summary>A newer release published on GitHub.</summary>
public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string Notes,
    string PageUrl,
    string SetupName,
    string SetupUrl,
    long SetupSize,
    string? Sha256,
    string? Sha256Url);

public sealed class UpdateVerifyException(string message) : Exception(message);

/// <summary>
/// Checks the GitHub "latest release" of Freezia, downloads the installer, verifies it (SHA-256, and the
/// Authenticode signer when this copy is signed) and runs it silently. This is the only network access
/// in the app: it talks to api.github.com / github.com and sends nothing but a plain HTTP request.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string Repository = "tinnapatbooranabanyat/freezia-shot";
    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    /// <summary>FREEZIA_UPDATE_URL overrides the release API (local tests).</summary>
    public static string ApiUrl =>
        Environment.GetEnvironmentVariable("FREEZIA_UPDATE_URL") is { Length: > 0 } u
            ? u
            : $"https://api.github.com/repos/{Repository}/releases/latest";

    private static readonly Regex SetupAsset = new(@"^Freezia-Setup-[0-9][0-9.]*\.exe$", RegexOptions.IgnoreCase);

    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan, // per-request timeouts below; downloads can be slow
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Freezia-Updater", AppInfo.Version));
    }

    public static Version CurrentVersion => Version.TryParse(AppInfo.Version, out var v) ? v : new Version(1, 0, 0);

    /// <summary>Newer release found by the last check (null = up to date / not checked).</summary>
    public UpdateInfo? Available { get; private set; }

    public DateTime? LastCheck { get; private set; }

    /// <summary>True when this copy was installed by Freezia Setup (an uninstaller sits next to the exe).</summary>
    public static bool IsInstalled
    {
        get
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory);
            return dir != null && File.Exists(Path.Combine(dir, "unins000.exe"));
        }
    }

    // ---------------------------------------------------------------- check

    /// <summary>Ask GitHub for the latest release. Returns the update when it is newer than this copy.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        using var req = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // no published release yet
            LastCheck = DateTime.Now;
            Available = null;
            return null;
        }
        resp.EnsureSuccessStatusCode();
        string json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        LastCheck = DateTime.Now;
        Available = Parse(json, CurrentVersion);
        return Available;
    }

    /// <summary>Parse a GitHub release JSON; null if it is not newer than <paramref name="current"/> or has no installer.</summary>
    public static UpdateInfo? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        version = Normalize(version);
        if (version <= Normalize(current)) return null;

        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;

        if (!root.TryGetProperty("assets", out var assets)) return null;
        JsonElement? setup = null;
        string? shaUrl = null;
        foreach (var a in assets.EnumerateArray())
        {
            string name = a.GetProperty("name").GetString() ?? "";
            if (SetupAsset.IsMatch(name)) setup = a;
        }
        if (setup is not JsonElement s) return null;
        string setupName = s.GetProperty("name").GetString()!;
        foreach (var a in assets.EnumerateArray())
            if (string.Equals(a.GetProperty("name").GetString(), setupName + ".sha256", StringComparison.OrdinalIgnoreCase))
                shaUrl = a.GetProperty("browser_download_url").GetString();

        // GitHub publishes "digest": "sha256:<hex>" for every asset
        string? sha = null;
        if (s.TryGetProperty("digest", out var dg) && dg.GetString() is string digest &&
            digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            sha = digest.Substring(7).Trim().ToLowerInvariant();

        return new UpdateInfo(version, tag, notes, page, setupName,
            s.GetProperty("browser_download_url").GetString()!,
            s.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0,
            sha, shaUrl);
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    // ---------------------------------------------------------------- download + verify

    public static string DownloadDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "updates");

    /// <summary>Remove installers left from earlier updates.</summary>
    public static void CleanDownloads()
    {
        try
        {
            if (Directory.Exists(DownloadDir)) Directory.Delete(DownloadDir, true);
        }
        catch (Exception ex)
        {
            Logger.Error("Cleaning old update downloads failed", ex);
        }
    }

    /// <summary>Download the installer and verify it. Progress is 0..1. Returns the local path.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        string? expected = info.Sha256;
        if (expected == null && info.Sha256Url != null)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            string text = await _http.GetStringAsync(info.Sha256Url, cts.Token).ConfigureAwait(false);
            expected = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        }
        if (expected == null || !Regex.IsMatch(expected, "^[0-9a-f]{64}$"))
            throw new UpdateVerifyException("The release has no SHA-256 checksum for the installer.");

        Directory.CreateDirectory(DownloadDir);
        string target = Path.Combine(DownloadDir, info.SetupName);
        string part = target + ".part";

        using (var resp = await _http.GetAsync(info.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? info.SetupSize;
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long done = 0;
                int n;
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                while (true)
                {
                    idle.CancelAfter(TimeSpan.FromSeconds(60)); // stalled connection
                    n = await src.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                    if (n <= 0) break;
                    sha.AppendData(buffer, 0, n);
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    if (total > 0) progress?.Report(Math.Min(1, (double)done / total));
                }
            }
            string actual = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            if (actual != expected)
            {
                TryDelete(part);
                throw new UpdateVerifyException("The downloaded installer is damaged (SHA-256 mismatch).");
            }
        }

        // When this copy is code-signed, the update must be signed by the same publisher.
        string? self = Environment.ProcessPath;
        string? mySigner = self != null ? Authenticode.ValidSigner(self) : null;
        if (mySigner != null)
        {
            string? theirs = Authenticode.ValidSigner(part);
            if (theirs != mySigner)
            {
                TryDelete(part);
                throw new UpdateVerifyException(theirs == null
                    ? "The downloaded installer is not validly signed."
                    : $"The downloaded installer is signed by a different publisher ({theirs}).");
            }
        }

        File.Move(part, target, overwrite: true);
        progress?.Report(1);
        return target;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }

    // ---------------------------------------------------------------- install

    /// <summary>
    /// Run the installer silently (it shows a progress window, closes this app, upgrades in place and
    /// starts Freezia again: see /UPDATE in installer/Freezia.iss).
    /// </summary>
    public static void LaunchInstaller(string setupPath)
    {
        string lang = Loc.Language == AppLanguage.English ? "english" : "thai";
        Process.Start(new ProcessStartInfo(setupPath, $"/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1 /LANG={lang} {InstallModeArgument()}")
        {
            UseShellExecute = true, // lets Setup ask for elevation for an all-users install
        });
    }

    /// <summary>
    /// Upgrade the same installation this copy runs from: "just me" installs are registered under HKCU,
    /// "all users" under HKLM (Setup then asks for elevation).
    /// </summary>
    public static string InstallModeArgument()
    {
        try
        {
            string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppInfo.InstallerAppId}_is1");
            if (key?.GetValue("InstallLocation") is string loc && exeDir != null &&
                string.Equals(Path.GetFullPath(loc).TrimEnd('\\'), Path.GetFullPath(exeDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return "/CURRENTUSER";
        }
        catch (Exception ex)
        {
            Logger.Error("Reading the install location failed", ex);
        }
        return "/ALLUSERS";
    }

    public static void OpenReleasePage(string? url = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url ?? ReleasesPage) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error("Opening the release page failed", ex);
        }
    }

    /// <summary>Release notes as plain text (light Markdown cleanup).</summary>
    public static string PlainNotes(string markdown)
    {
        var all = markdown.Replace("\r\n", "\n").Split('\n');
        // release.yml writes "## What's new" then "## Install": show only the changes
        int start = Array.FindIndex(all, l => Regex.IsMatch(l, @"^\s*#+\s*What's new", RegexOptions.IgnoreCase));
        if (start >= 0)
        {
            int end = Array.FindIndex(all, start + 1, l => Regex.IsMatch(l, @"^\s*#+\s"));
            all = all[(start + 1)..(end < 0 ? all.Length : end)];
        }
        var lines = all.Select(l =>
        {
            l = Regex.Replace(l, @"\[([^\]]+)\]\([^)]+\)", "$1"); // [text](url) -> text
            l = l.Replace("**", "").Replace("`", "");
            l = Regex.Replace(l, @"^\s*#+\s*", "");
            l = Regex.Replace(l, @"^\s*[-*]\s+", "• ");
            return l.TrimEnd();
        });
        return string.Join("\n", lines).Trim();
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Authenticode (WinVerifyTrust) check, offline (no revocation lookups).</summary>
internal static class Authenticode
{
    /// <summary>Subject of the signing certificate when the file has a valid signature, else null.</summary>
    public static string? ValidSigner(string path)
    {
        try
        {
            if (!IsValid(path)) return null;
#pragma warning disable SYSLIB0057
            using var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return cert.Subject;
        }
        catch
        {
            return null;
        }
    }

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);

    private static bool IsValid(string path)
    {
        IntPtr pathPtr = Marshal.StringToCoTaskMemUni(path);
        var file = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = pathPtr };
        IntPtr filePtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2,          // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                dwUnionChoice = 1,       // WTD_CHOICE_FILE
                pFile = filePtr,
                dwProvFlags = 0x1000,    // WTD_CACHE_ONLY_URL_RETRIEVAL: no network
            };
            var action = GenericVerifyV2;
            // dwStateAction = WTD_STATEACTION_IGNORE: no state handle to close afterwards
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(filePtr);
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }
}
