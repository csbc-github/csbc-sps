using System.Runtime.InteropServices;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>Native OneClick functions, using the application's existing SDK and global lifetime.</summary>
internal sealed class HoleCaptureNativeSdk : IHoleCaptureSdk
{
    public int Initialize() { Ljs8Runtime.EnsureInitialized(); return 0; }
    public int Open(ref HoleNativeEthernetConfig config) => Native.EthernetOpen(0, ref config);
    public int GetActiveProgram(ref byte programNo) => Native.GetActiveProgram(0, ref programNo);
    public int ChangeActiveProgram(byte programNo) => Native.ChangeActiveProgram(0, programNo);
    public int Trigger() => Native.Trigger(0);
    public int Close() => Native.CommunicationClose(0);

    public HoleCaptureReadResult Read(HoleNativeHeightRequest request)
    {
        if (request.ProfileCount is < 1 or > HoleCaptureAcquisition.RequestedProfiles)
            throw new ArgumentOutOfRangeException(nameof(request));
        // The native function has no buffer length: reserve the maximum width.
        var headers = new HoleNativeProfileHeader[request.ProfileCount];
        var heights = new ushort[checked(HeightFramePlyExporter.MaxWidth * request.ProfileCount)];
        var luminance = new byte[heights.Length];
        using var headerPin = new Pinned(headers);
        using var heightPin = new Pinned(heights);
        using var luminancePin = new Pinned(luminance);
        int rc = Native.GetHeightImageSimpleArray(0, ref request, 0, out var response, out var info,
            headerPin.Pointer, heightPin.Pointer, luminancePin.Pointer);
        if (rc != 0 || response.ProfileCount == 0) return new(rc, response, null);
        return new(rc, response, HoleCaptureFrameBuilder.Build(request, response, info, headers, heights, luminance));
    }

    private sealed class Pinned : IDisposable
    {
        private GCHandle handle;
        internal Pinned(Array array) => handle = GCHandle.Alloc(array, GCHandleType.Pinned);
        internal IntPtr Pointer => handle.AddrOfPinnedObject();
        public void Dispose() { if (handle.IsAllocated) handle.Free(); }
    }

    private static class Native
    {
        private const string Dll = "LJS8_IF.dll";
        [DllImport(Dll, EntryPoint = "LJS8IF_EthernetOpen", ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern int EthernetOpen(int id, ref HoleNativeEthernetConfig config);
        [DllImport(Dll, EntryPoint = "LJS8IF_GetActiveProgram", ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern int GetActiveProgram(int id, ref byte programNo);
        [DllImport(Dll, EntryPoint = "LJS8IF_ChangeActiveProgram", ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern int ChangeActiveProgram(int id, byte programNo);
        [DllImport(Dll, EntryPoint = "LJS8IF_Trigger", ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern int Trigger(int id);
        [DllImport(Dll, EntryPoint = "LJS8IF_CommunicationClose", ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern int CommunicationClose(int id);
        [DllImport(Dll, EntryPoint = "LJS8IF_GetHeightImageSimpleArray", ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern int GetHeightImageSimpleArray(int id, ref HoleNativeHeightRequest request, byte usePcImageFilter,
            out HoleNativeHeightResponse response, out HoleNativeHeightInfo info, IntPtr headers, IntPtr heights, IntPtr luminance);
    }
}
