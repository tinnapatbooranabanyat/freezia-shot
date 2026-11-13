using System;
using System.IO;
using System.Runtime.InteropServices;
using SnapEdit.Native;

namespace SnapEdit.Core;

public static class KnownFolders
{
    private static readonly Guid FOLDERID_Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>The user's Downloads folder from the shell (honours a relocated folder).</summary>
    public static string Downloads
    {
        get
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                int hr = NativeMethods.SHGetKnownFolderPath(FOLDERID_Downloads, 0, IntPtr.Zero, out p);
                if (hr == 0 && p != IntPtr.Zero)
                {
                    string? path = Marshal.PtrToStringUni(p);
                    if (!string.IsNullOrWhiteSpace(path)) return path;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("SHGetKnownFolderPath(Downloads) failed", ex);
            }
            finally
            {
                if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
            }
            // Fallback only if the shell call fails.
            return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        }
    }

    /// <summary>Folder screenshots are saved to (custom or Downloads).</summary>
    public static string ResolveSaveFolder(AppSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.SaveFolder))
        {
            try
            {
                Directory.CreateDirectory(s.SaveFolder);
                return s.SaveFolder;
            }
            catch (Exception ex)
            {
                Logger.Error($"Custom save folder unusable: {s.SaveFolder}", ex);
            }
        }
        string d = Downloads;
        Directory.CreateDirectory(d);
        return d;
    }
}
