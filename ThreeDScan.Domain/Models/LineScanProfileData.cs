namespace ThreeDScan.Domain.Models;

/// <summary>POST /api/scan/profile 回應本體（對齊 Hank ProfileCapture）。</summary>
public sealed class LineScanProfileData
{
    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; init; } = "";

    /// <summary>輪廓點 X（scanner_xy mm）。</summary>
    public double[] X { get; init; } = [];

    /// <summary>輪廓點 Y。</summary>
    public double[] Y { get; init; } = [];

    /// <summary>擷取時刻 ISO-8601。</summary>
    public string CapturedAt { get; init; } = "";

    /// <summary>單位。</summary>
    public string Units { get; init; } = "mm";

    /// <summary>座標系。</summary>
    public string CoordinateFrame { get; init; } = "scanner_xy";

    /// <summary>平均圈數。</summary>
    public int AverageScans { get; init; }

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; init; } = "";

    /// <summary>最後一帧 Encoder。</summary>
    public IReadOnlyList<LineScanEncoderData> Encoders { get; init; } = Array.Empty<LineScanEncoderData>();
}

/// <summary>Encoder 讀值。</summary>
public sealed class LineScanEncoderData
{
    /// <summary>位置。</summary>
    public uint Position { get; init; }

    /// <summary>保留。</summary>
    public ushort Reserved { get; init; }
}
