using Keyence.LjDevCommunication;

namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>組成 LJ Get/SetSetting 的目標位址。</summary>
internal static class Ljs8TargetBuilder
{
    /// <summary>組成單一程式設定目標。</summary>
    public static Ljs8IfTargetSetting ProgramItem(byte programNo, byte category, byte item)
    {
        return new Ljs8IfTargetSetting
        {
            _type = (byte)(0x10 + programNo),
            _category = category,
            _item = item,
        };
    }

    /// <summary>組成程式批次設定目標。</summary>
    public static Ljs8IfTargetSetting ProgramBatch(byte programNo)
    {
        return ProgramItem(programNo, 0xFF, 0x00);
    }
}
