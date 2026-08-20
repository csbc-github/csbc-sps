namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>Cat / Item 對照 LJ-S8000 通訊程式庫 §10.3.3（各程式內設定）。</summary>
internal static class Ljs8CaptureSettingTargets
{
    // 拍攝設定：間隔點數（Category 0x03）
    public const byte CatInterval = 0x03;
    public const byte ItemIntervalY = 0x01;          // 1 byte，1~8（1=不縮減）

    // 拍攝設定：曝光 / 動態量程 / 光量控制（Category 0x04）
    public const byte CatImaging = 0x04;
    public const byte ItemExposureTime = 0x00;       // 0~15
    public const byte ItemDynamicRange = 0x01;       // 1~9
    public const byte ItemLightControlMode = 0x02;   // 0:MANUAL 1:AUTO 2:SLOPE
    public const byte ItemLightControlUpper = 0x03;  // 1~99
    public const byte ItemLightControlLower = 0x04;  // 1~99

    // 拍攝設定：峰值 / 半透明（Category 0x05）
    public const byte CatPeak = 0x05;
    public const byte ItemPeakSensitivity = 0x00;    // 1 byte，1~5
    public const byte ItemPeakSelection = 0x01;      // 16 byte（byte0 = 峰值選擇 0~5）
    public const byte ItemTranslucent = 0x02;        // 讀 1 byte；單項 SetSetting 不支援，須批次 blob 修補
    public const byte ItemPeakWidthFilter = 0x03;    // 2 byte

    // 拍攝設定：死角資料插補（Category 0x06，1 byte）
    public const byte CatProfile = 0x06;
    public const byte ItemDeadZoneInterpolation = 0x00; // 0:不插補 1:水平垂直 2:線性

    public const int PeakSelectionDataLength = 16;
    public const int PeakWidthFilterDataLength = 2;
    /// <summary>SetSetting 時各項目的 buffer 長度（§10.3.3 + 實測）。</summary>
    public static int GetSetDataLength(byte category, byte item)
    {
        switch (category, item)
        {
            case (CatImaging, _):
                return 4; // 手冊讀 1 byte，SetSetting 實測須 4 byte
            case (CatInterval, _):
                return 4;
            case (CatPeak, ItemPeakSelection):
                return PeakSelectionDataLength;
            case (CatPeak, ItemPeakWidthFilter):
                return 4; // 手冊讀 2 byte（byte0/1），SetSetting 實測須 4 byte
            case (CatPeak, _):
                return 4; // Item00 靈敏度
            case (CatProfile, _):
                return 4;
            default:
                return 4;
        }
    }
}
