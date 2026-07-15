namespace ThreeDScan.Domain.Models;

/// <summary>LJ-S8000 掃描器拍攝參數（對應 Navigator Scan 程式設定，不含 PC 圖像過濾）。Cat/Item 依 LJ-S 通訊程式庫 §10.3.3。</summary>
public sealed class ScannerCaptureSettings
{
    public byte ProgramNo { get; init; }

    // 感光特性
    /// <summary>動態範圍 1～9（Cat 0x04 Item 0x01）。</summary>
    public byte DynamicRange { get; set; } = 4;

    // 光量設定
    /// <summary>曝光時間索引 0～15（Cat 0x04 Item 0x00）。</summary>
    public byte ExposureTimeIndex { get; set; }

    /// <summary>光量控制模式：0=手動、1=自動、2=SLOPE（Cat 0x04 Item 0x02）。</summary>
    public byte LightControlMode { get; set; }

    /// <summary>光量控制上限 1～99（Cat 0x04 Item 0x03）。</summary>
    public byte LightControlUpper { get; set; } = 99;

    /// <summary>光量控制下限 1～99（Cat 0x04 Item 0x04）。</summary>
    public byte LightControlLower { get; set; } = 1;

    // 峰值處理
    /// <summary>峰值檢測靈敏度 1～5（Cat 0x05 Item 0x00）。</summary>
    public byte PeakDetectionSensitivity { get; set; } = 4;

    /// <summary>半透明體峰值處理（Cat 0x05 Item 0x02）。</summary>
    public bool TranslucentPeakProcessing { get; set; }

    // 複數峰值時處理
    /// <summary>光暈過濾器（峰值寬度過濾器）ON/OFF（Cat 0x05 Item 0x03 byte0）。</summary>
    public bool HaloFilterEnabled { get; set; }

    /// <summary>光暈過濾器強度 1～5（Cat 0x05 Item 0x03 byte1）。</summary>
    public byte HaloFilterStrength { get; set; } = 3;

    /// <summary>峰值選擇 0～5（Cat 0x05 Item 0x01 之 byte0）。</summary>
    public byte PeakSelection { get; set; }

    // 死角
    /// <summary>死角資料差補：0=關閉、1=水平/垂直、2=線性（Cat 0x06 Item 0x00）。</summary>
    public byte DeadZoneInterpolation { get; set; }

    // 間隔倍數
    /// <summary>Y 方向間隔倍數：UI 0=OFF、2～8；SDK 間隔點數-Y 1～8（Cat 0x03 Item 0x01）。</summary>
    public byte YIntervalMultiplier { get; set; } = 4;

    /// <summary>Y 方向數據間隔 (mm)，Navigator 僅顯示、不可調；程式不自動寫入控制器。</summary>
    public double YDataIntervalMm { get; set; } = 0.8;
}
