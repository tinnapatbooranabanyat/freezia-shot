using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Freezia.Core;

namespace Freezia.Capture;

/// <summary>
/// Monitor capture with Windows.Graphics.Capture (DWM-composed frames). Unlike GDI it captures
/// hardware-accelerated / full-screen game content, and on HDR monitors it captures FP16 and maps the
/// colors back to SDR with the monitor's "SDR content brightness" so the result isn't washed out.
/// Direct3D 11 is used through raw COM (no extra libraries).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.18362.0")]
public static unsafe class WgcCapture
{
    private static readonly object Gate = new();
    private static IntPtr _device, _context;
    private static IDirect3DDevice? _winrtDevice;
    private static bool _accessRequested;

    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    [StructLayout(LayoutKind.Sequential)]
    private struct Texture2DDesc
    {
        public uint Width, Height, MipLevels, ArraySize;
        public int Format;
        public uint SampleCount, SampleQuality;
        public int Usage;
        public uint BindFlags, CPUAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MappedSubresource { public IntPtr Data; public uint RowPitch; public uint DepthPitch; }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels,
        uint numFeatureLevels, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    /// <summary>Windows 10 1903+ with a working GPU path.</summary>
    public static bool IsSupported
    {
        get
        {
            try
            {
                return GraphicsCaptureSession.IsSupported();
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Create the D3D device (and ask for borderless capture) ahead of the first PrintScreen.</summary>
    public static void Warmup()
    {
        try
        {
            lock (Gate)
            {
                EnsureDevice();
                RequestBorderless();
            }
        }
        catch (Exception ex)
        {
            Logger.Info("Capture warm-up failed (GDI will be used): " + ex.Message);
        }
    }

    private static void EnsureDevice()
    {
        if (_winrtDevice != null) return;
        const int D3D_DRIVER_TYPE_HARDWARE = 1;
        const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
        Check(D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            IntPtr.Zero, 0, 7, out _device, out _, out _context), "D3D11CreateDevice");
        var iid = IID_IDXGIDevice;
        Check(Marshal.QueryInterface(_device, in iid, out IntPtr dxgi), "QueryInterface(IDXGIDevice)");
        try
        {
            Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out IntPtr inspectable), "CreateDirect3D11DeviceFromDXGIDevice");
            _winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            Marshal.Release(inspectable);
        }
        finally
        {
            Marshal.Release(dxgi);
        }
    }

    private static GraphicsCaptureItem ItemForMonitor(IntPtr hmonitor)
    {
        var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var iid = IID_IGraphicsCaptureItemInterop;
        Check(Marshal.QueryInterface(factory.ThisPtr, in iid, out IntPtr interop), "QueryInterface(IGraphicsCaptureItemInterop)");
        try
        {
            var itemIid = IID_IGraphicsCaptureItem;
            IntPtr item;
            // IGraphicsCaptureItemInterop::CreateForMonitor is vtable slot 4 (after IUnknown + CreateForWindow)
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)(*(IntPtr**)interop)[4];
            Check(fn(interop, hmonitor, &itemIid, &item), "CreateForMonitor");
            var result = GraphicsCaptureItem.FromAbi(item);
            Marshal.Release(item);
            return result;
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    /// <summary>On Windows 11 ask once for borderless capture (no yellow frame flashing around the screen).</summary>
    private static void RequestBorderless()
    {
        if (_accessRequested) return;
        _accessRequested = true;
        try
        {
            if (ApiInformation.IsMethodPresent("Windows.Graphics.Capture.GraphicsCaptureAccess", "RequestAccessAsync"))
                GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless).AsTask().Wait(1000);
        }
        catch (Exception ex)
        {
            Logger.Info("Borderless capture request failed: " + ex.Message);
        }
    }

    /// <summary>Capture one monitor into an opaque Pbgra32 image (origin = monitor position).</summary>
    public static PixelImage CaptureMonitor(MonitorInfo mon)
    {
        lock (Gate)
        {
            EnsureDevice();
            RequestBorderless();
            var hdr = DisplayInfo.ForDevice(mon.DeviceName);
            var format = hdr.Enabled ? DirectXPixelFormat.R16G16B16A16Float : DirectXPixelFormat.B8G8R8A8UIntNormalized;

            var item = ItemForMonitor(mon.Handle);
            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice!, format, 1, item.Size);
            using var session = pool.CreateCaptureSession(item);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                try { session.IsCursorCaptureEnabled = false; } catch { /* not available */ }
            }
            if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
            {
                try { session.IsBorderRequired = false; } catch { /* not allowed: keep the border */ }
            }

            using var arrived = new ManualResetEventSlim(false);
            pool.FrameArrived += (_, _) => arrived.Set();
            session.StartCapture();
            if (!arrived.Wait(1500)) throw new TimeoutException("No frame from Windows.Graphics.Capture");
            using var frame = pool.TryGetNextFrame() ?? throw new InvalidOperationException("Empty capture frame");
            return ReadFrame(frame.Surface, mon, hdr);
        }
    }

    private static PixelImage ReadFrame(IDirect3DSurface surface, MonitorInfo mon, DisplayInfo.Hdr hdr)
    {
        IntPtr texture = SurfaceTexture(surface);
        IntPtr staging = IntPtr.Zero;
        try
        {
            Texture2DDesc desc;
            ((delegate* unmanaged[Stdcall]<IntPtr, Texture2DDesc*, void>)(*(IntPtr**)texture)[10])(texture, &desc); // GetDesc
            var sdesc = desc;
            sdesc.MipLevels = 1;
            sdesc.ArraySize = 1;
            sdesc.SampleCount = 1;
            sdesc.SampleQuality = 0;
            sdesc.Usage = 3;               // D3D11_USAGE_STAGING
            sdesc.BindFlags = 0;
            sdesc.CPUAccessFlags = 0x20000; // D3D11_CPU_ACCESS_READ
            sdesc.MiscFlags = 0;
            IntPtr st;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, Texture2DDesc*, IntPtr, IntPtr*, int>)(*(IntPtr**)_device)[5])(_device, &sdesc, IntPtr.Zero, &st), "CreateTexture2D");
            staging = st;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)(*(IntPtr**)_context)[47])(_context, staging, texture); // CopyResource

            MappedSubresource map;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, uint, MappedSubresource*, int>)(*(IntPtr**)_context)[14])(_context, staging, 0, 1, 0, &map), "Map");
            try
            {
                int w = (int)desc.Width, h = (int)desc.Height;
                var px = new byte[w * h * 4];
                byte* src = (byte*)map.Data;
                if (desc.Format == 10) // DXGI_FORMAT_R16G16B16A16_FLOAT (scRGB, linear, 1.0 = 80 nits)
                    ToneMap(src, (int)map.RowPitch, px, w, h, 80.0 / Math.Max(80, hdr.SdrWhiteNits));
                else
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy((IntPtr)(src + y * map.RowPitch), px, y * w * 4, w * 4);
                        for (int i = y * w * 4 + 3; i < (y + 1) * w * 4; i += 4) px[i] = 255;
                    }
                return new PixelImage(w, h, px, mon.Bounds.X, mon.Bounds.Y);
            }
            finally
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)(*(IntPtr**)_context)[15])(_context, staging, 0); // Unmap
            }
        }
        finally
        {
            if (staging != IntPtr.Zero) Marshal.Release(staging);
            Marshal.Release(texture);
        }
    }

    /// <summary>ID3D11Texture2D behind a WinRT IDirect3DSurface.</summary>
    private static IntPtr SurfaceTexture(IDirect3DSurface surface)
    {
        var obj = ((WinRT.IWinRTObject)surface).NativeObject;
        var iid = IID_IDirect3DDxgiInterfaceAccess;
        Check(Marshal.QueryInterface(obj.ThisPtr, in iid, out IntPtr access), "QueryInterface(IDirect3DDxgiInterfaceAccess)");
        try
        {
            var texIid = IID_ID3D11Texture2D;
            IntPtr tex;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)(*(IntPtr**)access)[3])(access, &texIid, &tex), "GetInterface(ID3D11Texture2D)");
            return tex;
        }
        finally
        {
            Marshal.Release(access);
        }
    }

    /// <summary>scRGB FP16 → sRGB 8-bit: scale so SDR white = 1.0, clip highlights, apply the sRGB curve.</summary>
    private static void ToneMap(byte* src, int pitch, byte[] dst, int w, int h, double scale)
    {
        var lut = new byte[4097];
        for (int i = 0; i <= 4096; i++)
        {
            double v = i / 4096.0;
            double s = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            lut[i] = (byte)Math.Clamp(Math.Round(s * 255), 0, 255);
        }
        byte Enc(Half hv)
        {
            double v = (double)(float)hv * scale;
            if (!(v > 0)) return 0;
            return v >= 1 ? (byte)255 : lut[(int)(v * 4096)];
        }
        for (int y = 0; y < h; y++)
        {
            Half* row = (Half*)(src + y * pitch);
            int di = y * w * 4;
            for (int x = 0; x < w; x++, di += 4)
            {
                dst[di + 2] = Enc(row[x * 4]);     // R
                dst[di + 1] = Enc(row[x * 4 + 1]); // G
                dst[di] = Enc(row[x * 4 + 2]);     // B
                dst[di + 3] = 255;
            }
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new COMException($"{what} failed (0x{hr:X8})", hr);
    }
}
