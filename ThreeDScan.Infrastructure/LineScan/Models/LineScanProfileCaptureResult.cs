namespace ThreeDScan.Infrastructure.LineScan.Models;

/// <summary>單次 profile 擷取結果（裝置層）。</summary>
public sealed class LineScanProfileCaptureResult
{
    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; init; } = "";

    /// <summary>有效點 X（scanner XY mm）。</summary>
    public double[] X { get; init; } = [];

    /// <summary>有效點 Y。</summary>
    public double[] Y { get; init; } = [];

    /// <summary>擷取完成時刻。</summary>
    public DateTimeOffset CapturedAt { get; init; }

    /// <summary>平均圈數。</summary>
    public int AverageScans { get; init; }

    /// <summary>最後一帧 Encoder 讀值。</summary>
    public IReadOnlyList<LineScanEncoderSample> Encoders { get; init; } = Array.Empty<LineScanEncoderSample>();
}

/// <summary>Encoder 通道樣本。</summary>
public sealed class LineScanEncoderSample
{
    /// <summary>位置計數。</summary>
    public uint Position { get; init; }

    /// <summary>保留欄。</summary>
    public ushort Reserved { get; init; }
}
