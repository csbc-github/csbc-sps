using System.Text.Json.Serialization;

namespace ThreeDScan.Application.Calibration.Dtos;

/// <summary>儀器校正 API 成功回應資料（對齊 API 串接規格）。</summary>
public sealed class InstrumentCalibrationData
{
    /// <summary>第一次拍攝完成、等待第二次時的 status 值。</summary>
    public const string StatusAwaitingNextShot = "AwaitingNextShot";

    /// <summary>掃描器識別碼。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScannerId { get; init; }

    /// <summary>本次請求追溯 ID（除錯／稽核用；Production 規格回應可不回）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; init; }

    /// <summary>補償值 offsetX/Y/Z (mm)。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompensationDto? Compensation { get; init; }

    /// <summary>反推的掃描器實際絕對位置。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CoordinateDto? ActualScannerPosition { get; init; }

    /// <summary>XY 品質指標：球距誤差 RMSE (mm)，門檻 &lt; 1.0。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double RmseXY { get; init; }

    /// <summary>
    /// Z 品質指標：兩次反推掃描器 Z 之絕對差 (mm)；門檻見 <c>ZDeviationThresholdMm</c>。
    /// Seq=1 時為 null。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ZDeviation { get; init; }

    /// <summary>校正完成時間（ISO 8601）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CalibratedAt { get; init; }

    /// <summary>SDK 量測到的所有保留球。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MeasuredBallDto>? Balls { get; init; }

    /// <summary>與台船傳入標準球配對後的明細。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MatchedBallDto>? MatchedBalls { get; init; }

    /// <summary>滾輪／基準面平面量測（測試用，LPlaneTool）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DatumPlaneDto? DatumPlane { get; init; }

    /// <summary>量測推算的掃描器到地板（滾輪面）Z 距離 (mm)。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double ScannerToFloorZ { get; init; }

    /// <summary>兩次拍攝任務 ID（第一次建立，第二次沿用）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaskId { get; init; }

    /// <summary>拍攝序：1＝第一幀（暫存），2＝合併完成。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Seq { get; init; }

    /// <summary>
    /// 任務狀態。第一次固定 <see cref="StatusAwaitingNextShot"/>；第二次為 null（規格不回此欄）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }
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
