using System.Text.Json;

namespace ThreeDScan.Domain.Models;

/// <summary>面掃描一段。對應 Hank <c>Scan3D.Service.SegmentDocument</c>。</summary>
public sealed class SegmentDocument
{
    /// <summary>段序（從 1）。</summary>
    public int Number { get; init; }

    /// <summary>呼叫端指定的段鍵。</summary>
    public string? SegmentKey { get; init; }

    /// <summary>請求指紋（SHA-256 hex）。</summary>
    public string RequestFingerprint { get; init; } = "";

    /// <summary>原始 PLY 路徑。</summary>
    public string RawPlyPath { get; init; } = "";

    /// <summary>該段處理結果 JSON 路徑。</summary>
    public string ProcessingResultPath { get; init; } = "";

    /// <summary>該段相機位姿。</summary>
    public Pose5 CameraPosition { get; init; } = new Pose5();

    /// <summary>擷取時刻。</summary>
    public DateTimeOffset CapturedAt { get; init; }

    /// <summary>該段 API 回應。</summary>
    public JsonElement Response { get; init; }
}
