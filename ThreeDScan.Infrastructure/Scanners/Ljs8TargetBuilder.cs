using Keyence.LjDevCommunication;

namespace ThreeDScan.Infrastructure.Scanners;

internal static class Ljs8TargetBuilder
{
    public static Ljs8IfTargetSetting ProgramItem(byte programNo, byte category, byte item)
    {
        return new Ljs8IfTargetSetting
        {
            _type = (byte)(0x10 + programNo),
            _category = category,
            _item = item,
        };
    }

    public static Ljs8IfTargetSetting ProgramBatch(byte programNo)
    {
        return ProgramItem(programNo, 0xFF, 0x00);
    }
}
