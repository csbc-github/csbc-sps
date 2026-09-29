namespace ThreeDScan.Domain.Models;

/// <summary>Pushed to WPF when instrument calibration API completes (LJScanWinform 量測按鈕).</summary>
public sealed class CalibrationPreview
{
    /// <summary>掃描器編號。</summary>
    public required string ScannerId { get; init; }

    /// <summary>追溯 ID。</summary>
    public required string TraceId { get; init; }

    /// <summary>推送時刻。</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>校正是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>失敗時的說明。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>量到的球。</summary>
    public IReadOnlyList<MeasuredBall> Balls { get; init; } = Array.Empty<MeasuredBall>();

    /// <summary>補償 X (mm)。</summary>
    public double? CompensationOffsetX { get; init; }

    /// <summary>補償 Y (mm)。</summary>
    public double? CompensationOffsetY { get; init; }

    /// <summary>補償 Z (mm)。</summary>
    public double? CompensationOffsetZ { get; init; }

    /// <summary>校正 RMSE (mm)。</summary>
    public double? Rmse { get; init; }

    /// <summary>相對 ContentsRoot，供 Api 靜態檔 URL 使用。</summary>
    public string? PreviewGrayRelativePath { get; init; }

    /// <summary>高度圖預覽相對路徑。</summary>
    public string? PreviewHeightRelativePath { get; init; }

    /// <summary>Contents 資料夾路徑。</summary>
    public string? ContentsFolder { get; init; }

    /// <summary>影像座標轉換。</summary>
    public CaptureImageGeometry? Geometry { get; init; }

    /// <summary>Ref 圖樣 X (mm)。</summary>
    public double? RefPatternX { get; init; }

    /// <summary>Ref 圖樣 Y (mm)。</summary>
    public double? RefPatternY { get; init; }

    /// <summary>Ref 球心 X (mm)。</summary>
    public double? RefSphereX { get; init; }

    /// <summary>Ref 球心 Y (mm)。</summary>
    public double? RefSphereY { get; init; }

    /// <summary>本次量測使用的預期球徑（DB 合併後）。</summary>
    public double? ExpectedBallDiameter { get; init; }

    /// <summary>球徑允許最大誤差 (mm)。</summary>
    public double? MaxDiameterError { get; init; }

    /// <summary>球徑容差 (mm)。</summary>
    public double? DiameterTolerance { get; init; }

    /// <summary>SDK PatternMatch 偵測上限（= standardBalls 數量）。</summary>
    public int? DetectCount { get; init; }

    /// <summary>配對明細（失敗時供 WPF 對照 known vs measured）。</summary>
    public IReadOnlyList<MatchedBallPreview> MatchedBalls { get; init; } = Array.Empty<MatchedBallPreview>();
}
