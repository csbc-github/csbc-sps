using ThreeDScan.Application.Calibration;

namespace ThreeDScan.Application.Calibration.Dtos;

/// <summary>儀器校正 API 成功回應資料。</summary>
public sealed class InstrumentCalibrationData
{
    /// <summary>掃描器識別碼。</summary>
    public required string ScannerId { get; init; }

    /// <summary>本次請求追溯 ID。</summary>
    public required string TraceId { get; init; }

    /// <summary>補償值 offsetX/Y/Z (mm)。</summary>
    public CompensationDto Compensation { get; init; } = new();

    /// <summary>反推的掃描器實際絕對位置。</summary>
    public CoordinateDto? ActualScannerPosition { get; init; }

    /// <summary>校正殘差 RMSE (mm)。</summary>
    public double Rmse { get; init; }

    /// <summary>校正完成時間（ISO 8601）。</summary>
    public string CalibratedAt { get; init; } = "";

    /// <summary>SDK 量測到的所有保留球。</summary>
    public IReadOnlyList<MeasuredBallDto> Balls { get; init; } = Array.Empty<MeasuredBallDto>();

    /// <summary>與台船傳入標準球配對後的明細。</summary>
    public IReadOnlyList<MatchedBallDto> MatchedBalls { get; init; } = Array.Empty<MatchedBallDto>();

    /// <summary>滾輪／基準面平面量測（測試用，LPlaneTool）。</summary>
    public DatumPlaneDto? DatumPlane { get; init; }

    /// <summary>量測推算的掃描器到地板（滾輪面）Z 距離 (mm)。</summary>
    public double ScannerToFloorZ { get; init; }
}

/// <summary>基準面（滾輪）平面量測結果。</summary>
public sealed class DatumPlaneDto
{
    public bool Ok { get; init; }

    public string? ErrorMessage { get; init; }

    public double PlaneA { get; init; }

    public double PlaneB { get; init; }

    public double PlaneC { get; init; }

    /// <summary>ROI 中心處基準面 Z（mm）。</summary>
    public double? PlaneZAtRegionCenter { get; init; }

    /// <summary>第一顆球心相對基準面高度（mm）。</summary>
    public double? FirstBallRelativeZ { get; init; }
}

/// <summary>三軸補償偏移量 (mm)。</summary>
public sealed class CompensationDto
{
    /// <summary>X 軸補償 (mm)。</summary>
    public double OffsetX { get; init; }

    /// <summary>Y 軸補償 (mm)。</summary>
    public double OffsetY { get; init; }

    /// <summary>Z 軸補償 (mm)。</summary>
    public double OffsetZ { get; init; }
}

/// <summary>單顆量測球結果（掃描器座標系）。</summary>
public sealed class MeasuredBallDto
{
    /// <summary>球索引（0 起）。</summary>
    public int Index { get; init; }

    /// <summary>球心 X (mm)。</summary>
    public double X { get; init; }

    /// <summary>球心 Y (mm)。</summary>
    public double Y { get; init; }

    /// <summary>球心 Z (mm)。</summary>
    public double Z { get; init; }

    /// <summary>擬合直徑 (mm)。</summary>
    public double Diameter { get; init; }

    /// <summary>直徑是否在允許誤差內。</summary>
    public bool Ok { get; init; }
}
