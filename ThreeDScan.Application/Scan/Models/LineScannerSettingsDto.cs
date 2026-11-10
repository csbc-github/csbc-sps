namespace ThreeDScan.Application.Scan.Models;

/// <summary>GET scanners/{id}/settings 回應（對齊 Hank OpenAPI）。</summary>
public sealed class LineScannerSettingsDto
{
    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; init; } = "";

    /// <summary>桌面兩點；未設定為 null。</summary>
    public LineScannerCalibrationDto? Calibration { get; init; }

    /// <summary>有效範圍 [xmin,xmax,ymin,ymax]；未框選為 null。</summary>
    public double[]? RoiXY { get; init; }

    /// <summary>長度單位。</summary>
    public string Units { get; init; } = "mm";

    /// <summary>座標系。</summary>
    public string CoordinateFrame { get; init; } = "scanner_xy";

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}

/// <summary>兩點桌面校正。</summary>
public sealed class LineScannerCalibrationDto
{
    /// <summary>第一點。</summary>
    public LineScannerPointDto First { get; init; } = new();

    /// <summary>第二點。</summary>
    public LineScannerPointDto Second { get; init; } = new();
}

/// <summary>掃描器平面座標點。</summary>
public sealed class LineScannerPointDto
{
    /// <summary>X (mm)。</summary>
    public double X { get; init; }

    /// <summary>Y (mm)。</summary>
    public double Y { get; init; }
}

/// <summary>POST two-point 請求。</summary>
public sealed class LineScannerTwoPointRequest
{
    /// <summary>第一點。</summary>
    public LineScannerPointDto First { get; init; } = new();

    /// <summary>第二點。</summary>
    public LineScannerPointDto Second { get; init; } = new();
}

/// <summary>POST roi 請求。</summary>
public sealed class LineScannerRoiRequest
{
    /// <summary>四元陣列或 null 清除。</summary>
    public double[]? Bounds { get; init; }
}
