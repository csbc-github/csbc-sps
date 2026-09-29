namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>LJ 設定緩衝的位元組讀寫。</summary>
internal static class Ljs8SettingBuffers
{
    /// <summary>配置 4-byte 設定緩衝。</summary>
    public static byte[] FourBytes()
    {
        return new byte[4];
    }

    /// <summary>讀取指定位元組。</summary>
    public static byte At(byte[] data, int index = 0)
    {
        return data[index];
    }

    /// <summary>寫入指定位元組。</summary>
    public static void Set(byte[] data, byte value, int index = 0)
    {
        data[index] = value;
    }

    /// <summary>以 little-endian 讀取 UInt16。</summary>
    public static ushort U16(byte[] data, int index = 0)
    {
        return (ushort)(data[index] | (data[index + 1] << 8));
    }

    /// <summary>以 little-endian 寫入 UInt16。</summary>
    public static void SetU16(byte[] data, ushort value, int index = 0)
    {
        data[index] = (byte)(value & 0xFF);
        data[index + 1] = (byte)((value >> 8) & 0xFF);
    }

    /// <summary>以 little-endian 讀取 UInt32。</summary>
    public static uint U32(byte[] data, int index = 0)
    {
        return (uint)(data[index]
            | (data[index + 1] << 8)
            | (data[index + 2] << 16)
            | (data[index + 3] << 24));
    }

    /// <summary>以 little-endian 寫入 UInt32。</summary>
    public static void SetU32(byte[] data, uint value, int index = 0)
    {
        data[index] = (byte)(value & 0xFF);
        data[index + 1] = (byte)((value >> 8) & 0xFF);
        data[index + 2] = (byte)((value >> 16) & 0xFF);
        data[index + 3] = (byte)((value >> 24) & 0xFF);
    }
}
