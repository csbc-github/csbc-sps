using System.Text.Json;

namespace ThreeDScan.Domain.Models;

/// <summary>
/// 掃描任務整份文件（一列 DocumentJson）。對應 Hank <c>Scan3D.Service.TaskDocument</c>。
/// </summary>
public sealed class TaskDocument
{
    /// <summary>任務編號。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>種類（Surface／Hole／Line／Calibration）。</summary>
    public string Kind { get; set; } = "";

    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>工單號。</summary>
    public string? JobId { get; set; }

    /// <summary>狀態（Processing／Completed／Failed／Aborted）。</summary>
    public string Status { get; set; } = "Processing";

    /// <summary>階段。</summary>
    public string Phase { get; set; } = "Created";

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; set; } = "";

    /// <summary>建立時刻。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新時刻。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>擷取完成時刻。</summary>
    public DateTimeOffset? CapturedAt { get; set; }

    /// <summary>回呼 URL。</summary>
    public string? CallbackUrl { get; set; }

    /// <summary>對外檔案基底 URL。</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>完成時的結果。</summary>
    public JsonElement? Result { get; set; }

    /// <summary>失敗內容。</summary>
    public TaskApiError? Error { get; set; }

    /// <summary>面掃描各段。</summary>
    public List<SegmentDocument> Segments { get; set; } = new List<SegmentDocument>();

    /// <summary>產物（鍵＝artifactId）。</summary>
    public Dictionary<string, ArtifactDocument> Artifacts { get; set; } = new Dictionary<string, ArtifactDocument>(StringComparer.Ordinal);

    /// <summary>附件 ZIP 的 artifactId。</summary>
    public string? AttachmentsArtifactId { get; set; }

    /// <summary>附件不可用原因。</summary>
    public string? AttachmentsUnavailableReason { get; set; }

    /// <summary>材料厚度（mm）。</summary>
    public double? MaterialThicknessMm { get; set; }

    /// <summary>拼接模式。</summary>
    public StitchMode StitchMode { get; set; }

    /// <summary>公稱對接間隙（mm）。</summary>
    public double NominalGapMm { get; set; }

    /// <summary>線掃描滾輪速度。</summary>
    public double? RollerSpeed { get; set; }

    /// <summary>線掃描工作階段。</summary>
    public string? LineSessionId { get; set; }

    /// <summary>背景工作原始請求。</summary>
    public JsonElement? WorkRequest { get; set; }

    /// <summary>已嘗試回呼次數。</summary>
    public int CallbackAttempts { get; set; }

    /// <summary>下次回呼時刻。</summary>
    public DateTimeOffset? CallbackNextAttemptAt { get; set; }

    /// <summary>回呼是否已送達。</summary>
    public bool CallbackDelivered { get; set; }

    /// <summary>最後一次回呼錯誤。</summary>
    public string? CallbackLastError { get; set; }

    /// <summary>是否重播檔案。</summary>
    public bool IsReplay { get; set; }

    /// <summary>建立任務時的座標轉換快照。</summary>
    public CoordinateTransformOptions CoordinatesSnapshot { get; set; } = new CoordinateTransformOptions();

    /// <summary>儀器校正提交（若此任務寫入補償）。</summary>
    public CalibrationCommitDocument? CalibrationCommit { get; set; }

    /// <summary>是否已到終態。</summary>
    public bool Terminal
    {
        get
        {
            return Status is "Completed" or "Failed" or "Aborted";
        }
    }
}
