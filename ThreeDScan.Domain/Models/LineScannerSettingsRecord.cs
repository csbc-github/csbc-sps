namespace ThreeDScan.Domain.Models;

/// <summary>線掃描器桌面兩點與 ROI（每台一筆，§九 O5）。</summary>
public sealed class LineScannerSettingsRecord
{
    /// <summary>線掃描器編號（與 LineScanners 鍵名相同）。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>桌面校正第一點 X（掃描器平面 mm）；未設為 null。</summary>
    public double? CalibrationFirstX { get; set; }

    /// <summary>桌面校正第一點 Y。</summary>
    public double? CalibrationFirstY { get; set; }

    /// <summary>桌面校正第二點 X。</summary>
    public double? CalibrationSecondX { get; set; }

    /// <summary>桌面校正第二點 Y。</summary>
    public double? CalibrationSecondY { get; set; }

    /// <summary>ROI xmin；全 null 表示無框選。</summary>
    public double? RoiXMin { get; set; }

    /// <summary>ROI xmax。</summary>
    public double? RoiXMax { get; set; }

    /// <summary>ROI ymin。</summary>
    public double? RoiYMin { get; set; }

    /// <summary>ROI ymax。</summary>
    public double? RoiYMax { get; set; }

    /// <summary>最後更新時刻。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>最後寫入追溯 ID。</summary>
    public string TraceId { get; set; } = "";

    /// <summary>是否已具備有效兩點桌面校正。</summary>
    public bool HasCalibration
    {
        get
        {
            return CalibrationFirstX.HasValue && CalibrationFirstY.HasValue
                && CalibrationSecondX.HasValue && CalibrationSecondY.HasValue;
        }
    }

    /// <summary>ROI 四元陣列；全欄位皆有值時回傳，否則 null。</summary>
    public double[]? RoiBoundsOrNull()
    {
        if (!RoiXMin.HasValue || !RoiXMax.HasValue || !RoiYMin.HasValue || !RoiYMax.HasValue)
        {
            return null;
        }

        return new[]
        {
            RoiXMin.Value,
            RoiXMax.Value,
            RoiYMin.Value,
            RoiYMax.Value,
        };
    }
}
