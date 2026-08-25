namespace ThreeDScan.Infrastructure.Scanners;

internal static class Ljs8SettingBuffers
{
    public static byte[] FourBytes()
    {
        return new byte[4];
    }

    public static byte At(byte[] data, int index = 0)
    {
        return data[index];
    }

    public static void Set(byte[] data, byte value, int index = 0)
    {
        data[index] = value;
    }

    public static ushort U16(byte[] data, int index = 0)
    {
        return (ushort)(data[index] | (data[index + 1] << 8));
    }

    public static void SetU16(byte[] data, ushort value, int index = 0)
    {
        data[index] = (byte)(value & 0xFF);
        data[index + 1] = (byte)((value >> 8) & 0xFF);
    }

    public static uint U32(byte[] data, int index = 0)
    {
        return (uint)(data[index]
            | (data[index + 1] << 8)
            | (data[index + 2] << 16)
            | (data[index + 3] << 24));
    }

    public static void SetU32(byte[] data, uint value, int index = 0)
    {
        data[index] = (byte)(value & 0xFF);
        data[index + 1] = (byte)((value >> 8) & 0xFF);
        data[index + 2] = (byte)((value >> 16) & 0xFF);
        data[index + 3] = (byte)((value >> 24) & 0xFF);
    }
}
