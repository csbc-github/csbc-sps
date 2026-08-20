namespace ThreeDScan.Domain.Models;

/// <summary>Pushed to WPF when instrument calibration API completes (LJScanWinform 量測按鈕).</summary>
public sealed class CalibrationPreview
{
    public required string ScannerId { get; init; }
    public required string TraceId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<MeasuredBall> Balls { get; init; } = Array.Empty<MeasuredBall>();
    public double? CompensationOffsetX { get; init; }
    public double? CompensationOffsetY { get; init; }
    public double? CompensationOffsetZ { get; init; }
    public double? Rmse { get; init; }

    /// <summary>相對 ContentsRoot，供 Api 靜態檔 URL 使用。</summary>
    public string? PreviewGrayRelativePath { get; init; }
    public string? PreviewHeightRelativePath { get; init; }
    public string? ContentsFolder { get; init; }
    public CaptureImageGeometry? Geometry { get; init; }
    public double? RefPatternX { get; init; }
    public double? RefPatternY { get; init; }
    public double? RefSphereX { get; init; }
    public double? RefSphereY { get; init; }

    /// <summary>本次量測使用的預期球徑（DB 合併後）。</summary>
    public double? ExpectedBallDiameter { get; init; }

    public double? MaxDiameterError { get; init; }

    public double? DiameterTolerance { get; init; }

    /// <summary>SDK PatternMatch 偵測上限（= standardBalls 數量）。</summary>
    public int? DetectCount { get; init; }

    /// <summary>配對明細（失敗時供 WPF 對照 known vs measured）。</summary>
    public IReadOnlyList<MatchedBallPreview> MatchedBalls { get; init; } = Array.Empty<MatchedBallPreview>();
}
