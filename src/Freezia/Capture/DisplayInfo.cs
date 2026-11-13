using System;
using System.Runtime.InteropServices;

namespace Freezia.Capture;

/// <summary>
/// HDR state of a monitor from the Windows display configuration API: whether "Use HDR" is on and the
/// "SDR content brightness" (SDR white level) used to map HDR captures back to normal (SDR) colors.
/// </summary>
public static class DisplayInfo
{
    public readonly record struct Hdr(bool Enabled, double SdrWhiteNits);

    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int GET_SOURCE_NAME = 1;
    private const int GET_ADVANCED_COLOR_INFO = 9;
    private const int GET_SDR_WHITE_LEVEL = 11;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathSourceInfo { public LUID AdapterId; public uint Id; public uint ModeInfoIdx; public uint StatusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathTargetInfo
    {
        public LUID AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public int OutputTechnology;
        public int Rotation;
        public int Scaling;
        public uint RefreshNumerator;
        public uint RefreshDenominator;
        public int ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo { public PathSourceInfo Source; public PathTargetInfo Target; public uint Flags; }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct ModeInfo { public int InfoType; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Header { public int Type; public uint Size; public LUID AdapterId; public uint Id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo { public Header Header; public uint Value; public int ColorEncoding; public uint BitsPerColorChannel; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdrWhiteLevel { public Header Header; public uint Level; }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PathInfo[] paths, ref uint numModes, [Out] ModeInfo[] modes, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SourceName info);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo info);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SdrWhiteLevel info);

    /// <summary>HDR info for a GDI device name such as "\\.\DISPLAY1" (from MONITORINFOEX).</summary>
    public static Hdr ForDevice(string gdiDeviceName)
    {
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0) return default;
            var paths = new PathInfo[np];
            var modes = new ModeInfo[nm];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return default;
            for (int i = 0; i < np; i++)
            {
                var src = new SourceName
                {
                    Header = new Header { Type = GET_SOURCE_NAME, Size = (uint)Marshal.SizeOf<SourceName>(), AdapterId = paths[i].Source.AdapterId, Id = paths[i].Source.Id },
                };
                if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;
                if (!string.Equals(src.GdiDeviceName, gdiDeviceName, StringComparison.OrdinalIgnoreCase)) continue;

                var color = new AdvancedColorInfo
                {
                    Header = new Header { Type = GET_ADVANCED_COLOR_INFO, Size = (uint)Marshal.SizeOf<AdvancedColorInfo>(), AdapterId = paths[i].Target.AdapterId, Id = paths[i].Target.Id },
                };
                bool enabled = DisplayConfigGetDeviceInfo(ref color) == 0 && (color.Value & 0x2) != 0; // advancedColorEnabled
                double nits = 80;
                var white = new SdrWhiteLevel
                {
                    Header = new Header { Type = GET_SDR_WHITE_LEVEL, Size = (uint)Marshal.SizeOf<SdrWhiteLevel>(), AdapterId = paths[i].Target.AdapterId, Id = paths[i].Target.Id },
                };
                if (DisplayConfigGetDeviceInfo(ref white) == 0 && white.Level > 0) nits = white.Level / 1000.0 * 80.0;
                return new Hdr(enabled, nits);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.Error("Display config query failed", ex);
        }
        return default;
    }
}
