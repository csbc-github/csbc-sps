namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 LineScannerSettings。</summary>
public sealed class LineScannerSettingsEntity
{
    /// <summary>線掃描器編號（主鍵）。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>桌面校正第一點 X。</summary>
    public double? CalibrationFirstX { get; set; }

    /// <summary>桌面校正第一點 Y。</summary>
    public double? CalibrationFirstY { get; set; }

    /// <summary>桌面校正第二點 X。</summary>
    public double? CalibrationSecondX { get; set; }

    /// <summary>桌面校正第二點 Y。</summary>
    public double? CalibrationSecondY { get; set; }

    /// <summary>ROI xmin。</summary>
    public double? RoiXMin { get; set; }

    /// <summary>ROI xmax。</summary>
    public double? RoiXMax { get; set; }

    /// <summary>ROI ymin。</summary>
    public double? RoiYMin { get; set; }

    /// <summary>ROI ymax。</summary>
    public double? RoiYMax { get; set; }

    /// <summary>最後更新。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; set; } = "";
}
