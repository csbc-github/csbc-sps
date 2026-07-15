using ThreeDScan.Common;
using ThreeDScan.Domain.Models;
using ThreeDScan.Models;

namespace ThreeDScan.ViewModels;

/// <summary>
/// 掃描器拍攝參數表單狀態，綁定 <c>ScannerSettingsWindow</c> 各輸入欄位。
/// 對應 <see cref="ScannerCaptureSettings"/>，負責 UI 雙向綁定與數值範圍限制。
/// </summary>
public sealed class ScannerCaptureSettingsFormViewModel : ViewModelBase
{
    private byte _dynamicRange = 4;
    private byte _exposureTimeIndex = 14;
    private byte _lightControlMode = 2;
    private byte _lightControlUpper = 99;
    private byte _lightControlLower = 1;
    private byte _peakDetectionSensitivity = 4;
    private bool _translucentPeakProcessing;
    private bool _haloFilterEnabled;
    private byte _haloFilterStrength = 3;
    private byte _peakSelection;
    private byte _deadZoneInterpolation = 1;
    private byte _yIntervalMultiplier = 4;

    /// <summary>曝光時間下拉選項（μs 標籤 → SDK 索引 0～15）。</summary>
    public IReadOnlyList<SelectOption<byte>> ExposureTimeOptions => ScannerCaptureSettingOptions.ExposureTimes;

    /// <summary>光量控制模式下拉選項（手動／自動／SLOPE）。</summary>
    public IReadOnlyList<SelectOption<byte>> LightControlModeOptions => ScannerCaptureSettingOptions.LightControlModes;

    /// <summary>峰值選擇下拉選項。</summary>
    public IReadOnlyList<SelectOption<byte>> PeakSelectionOptions => ScannerCaptureSettingOptions.PeakSelections;

    /// <summary>死角插補模式下拉選項。</summary>
    public IReadOnlyList<SelectOption<byte>> DeadZoneInterpolationOptions => ScannerCaptureSettingOptions.DeadZoneInterpolations;

    /// <summary>光暈過濾器強度下拉選項（1～5）。</summary>
    public IReadOnlyList<SelectOption<byte>> HaloFilterStrengthOptions => ScannerCaptureSettingOptions.HaloFilterStrengths;

    /// <summary>Y 方向間隔倍數下拉選項。</summary>
    public IReadOnlyList<SelectOption<byte>> YIntervalMultiplierOptions => ScannerCaptureSettingOptions.YIntervalMultipliers;

    /// <summary>動態範圍（1～9）。</summary>
    public byte DynamicRange
    {
        get => _dynamicRange;
        set => SetProperty(ref _dynamicRange, Math.Clamp(value, (byte)1, (byte)9));
    }

    /// <summary>曝光時間索引（0～15）。</summary>
    public byte ExposureTimeIndex
    {
        get => _exposureTimeIndex;
        set => SetProperty(ref _exposureTimeIndex, value);
    }

    /// <summary>光量控制模式：0=手動、1=自動、2=SLOPE。</summary>
    public byte LightControlMode
    {
        get => _lightControlMode;
        set
        {
            if (!SetProperty(ref _lightControlMode, value)) return;
            OnPropertyChanged(nameof(IsLightControlRangeEditable));
        }
    }

    /// <summary>光量控制為手動或 SLOPE 時，上下限才可編輯（自動模式由控制器調整）。</summary>
    public bool IsLightControlRangeEditable => LightControlMode != 1;

    /// <summary>光量控制上限（1～99）。</summary>
    public byte LightControlUpper
    {
        get => _lightControlUpper;
        set => SetProperty(ref _lightControlUpper, Math.Clamp(value, (byte)1, (byte)99));
    }

    /// <summary>光量控制下限（1～99）。</summary>
    public byte LightControlLower
    {
        get => _lightControlLower;
        set => SetProperty(ref _lightControlLower, Math.Clamp(value, (byte)1, (byte)99));
    }

    /// <summary>峰值檢測靈敏度（1～5）。</summary>
    public byte PeakDetectionSensitivity
    {
        get => _peakDetectionSensitivity;
        set => SetProperty(ref _peakDetectionSensitivity, Math.Clamp(value, (byte)1, (byte)5));
    }

    /// <summary>半透明體峰值處理開關。</summary>
    public bool TranslucentPeakProcessing
    {
        get => _translucentPeakProcessing;
        set => SetProperty(ref _translucentPeakProcessing, value);
    }

    /// <summary>光暈過濾器（峰值寬度過濾器）開關。</summary>
    public bool HaloFilterEnabled
    {
        get => _haloFilterEnabled;
        set => SetProperty(ref _haloFilterEnabled, value);
    }

    /// <summary>光暈過濾器強度（1～5）。</summary>
    public byte HaloFilterStrength
    {
        get => _haloFilterStrength;
        set => SetProperty(ref _haloFilterStrength, Math.Clamp(value, (byte)1, (byte)5));
    }

    /// <summary>峰值選擇（0～5）。</summary>
    public byte PeakSelection
    {
        get => _peakSelection;
        set => SetProperty(ref _peakSelection, value);
    }

    /// <summary>死角資料插補模式。</summary>
    public byte DeadZoneInterpolation
    {
        get => _deadZoneInterpolation;
        set => SetProperty(ref _deadZoneInterpolation, value);
    }

    /// <summary>Y 方向間隔倍數。</summary>
    public byte YIntervalMultiplier
    {
        get => _yIntervalMultiplier;
        set => SetProperty(ref _yIntervalMultiplier, value);
    }

    /// <summary>從控制器或資料庫讀取的設定填入表單各欄位。</summary>
    public void LoadFrom(ScannerCaptureSettings settings)
    {
        DynamicRange = settings.DynamicRange;
        ExposureTimeIndex = settings.ExposureTimeIndex;
        LightControlMode = settings.LightControlMode;
        LightControlUpper = settings.LightControlUpper;
        LightControlLower = settings.LightControlLower;
        PeakDetectionSensitivity = settings.PeakDetectionSensitivity;
        TranslucentPeakProcessing = settings.TranslucentPeakProcessing;
        HaloFilterEnabled = settings.HaloFilterEnabled;
        HaloFilterStrength = settings.HaloFilterStrength;
        PeakSelection = settings.PeakSelection;
        DeadZoneInterpolation = settings.DeadZoneInterpolation;
        YIntervalMultiplier = settings.YIntervalMultiplier;
    }

    /// <summary>將表單組裝為寫入控制器用的 <see cref="ScannerCaptureSettings"/>。</summary>
    /// <param name="programNo">LJ SDK 程式索引（0～15）。</param>
    /// <param name="yDataIntervalMm">Y 方向數據間隔（僅顯示用，寫入設定物件供記錄）。</param>
    public ScannerCaptureSettings ToSettings(byte programNo, double yDataIntervalMm) => new()
    {
        ProgramNo = programNo,
        DynamicRange = DynamicRange,
        ExposureTimeIndex = ExposureTimeIndex,
        LightControlMode = LightControlMode,
        LightControlUpper = LightControlUpper,
        LightControlLower = LightControlLower,
        PeakDetectionSensitivity = PeakDetectionSensitivity,
        TranslucentPeakProcessing = TranslucentPeakProcessing,
        HaloFilterEnabled = HaloFilterEnabled,
        HaloFilterStrength = HaloFilterStrength,
        PeakSelection = PeakSelection,
        DeadZoneInterpolation = DeadZoneInterpolation,
        YIntervalMultiplier = YIntervalMultiplier,
        YDataIntervalMm = yDataIntervalMm,
    };
}
