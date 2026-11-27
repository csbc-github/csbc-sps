using System.Net;
using System.Runtime.InteropServices;

namespace ThreeDScan.Infrastructure.Scanners;

// Blittable layouts from the supplied OneClick NativeMethods.cs / LJS8_IF.h.
[StructLayout(LayoutKind.Sequential)]
internal struct HoleNativeEthernetConfig
{
    internal byte Ip0, Ip1, Ip2, Ip3;
    internal ushort Port;
    private ushort reserved;

    internal static HoleNativeEthernetConfig Create(string address, ushort port)
    {
        if (!IPAddress.TryParse(address, out var ip)
            || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("請輸入有效的 IPv4 位址。", nameof(address));
        if (port == 0) throw new ArgumentOutOfRangeException(nameof(port));
        byte[] bytes = ip.GetAddressBytes();
        return new() { Ip0 = bytes[0], Ip1 = bytes[1], Ip2 = bytes[2], Ip3 = bytes[3], Port = port };
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct HoleNativeHeightRequest
{
    private byte reserved;
    internal byte PositionMode;
    private ushort reserved2;
    internal uint ImageNo, ProfileNo;
    internal ushort ProfileCount;
    internal byte Erase;
    private byte reserved3;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HoleNativeHeightResponse
{
    internal uint CurrentImageNo, CurrentProfileCount, OldestImageNo, OldestProfileCount;
    internal uint ImageNo, ImageProfileCount, TopProfileNo;
    internal ushort ProfileCount;
    internal byte CurrentImageCommitted;
    private byte reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HoleNativeHeightInfo
{
    internal ushort XPointCount, YLineCount;
    internal byte LuminanceOutput;
    private byte reserved0;
    private ushort reserved1;
    internal int XStart;
    internal uint PitchX;
    internal int YStart;
    internal uint PitchY;
    private uint reserved2;
    internal uint PitchZ;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HoleNativeProfileHeader
{
    private uint reserved;
    internal uint ImageNo, ProfileNo;
    internal byte ProcessingTimeout;
    private byte reserved1;
    private ushort reserved2;
    private uint reserved3, reserved4;
}
