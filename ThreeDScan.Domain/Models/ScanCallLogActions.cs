namespace ThreeDScan.Domain.Models;

/// <summary>
/// <see cref="ScanCallLogRecord.Action"/> 的固定值。
/// 與 <c>JobThickness</c> 無關：本表只記 API 呼叫舉證，厚度作業資料在工單表。
/// </summary>
public static class ScanCallLogActions
{
    /// <summary><c>POST /api/scan/line</c></summary>
    public const string ScanLine = "ScanLine";

    /// <summary><c>POST /api/scan/surface</c></summary>
    public const string ScanSurface = "ScanSurface";

    /// <summary><c>POST /api/scan/hole</c></summary>
    public const string ScanHole = "ScanHole";

    /// <summary><c>GET /api/scan/surface/{taskId}/attachments</c></summary>
    public const string SurfaceAttachments = "SurfaceAttachments";
}
