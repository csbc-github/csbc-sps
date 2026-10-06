namespace ThreeDScan.Domain.Models;

/// <summary>
/// <see cref="ScanCallLogRecord.Action"/> 的固定值。
/// </summary>
public static class ScanCallLogActions
{
    /// <summary><c>POST /api/scan/line/start</c></summary>
    public const string LineScanStart = "LineScanStart";

    /// <summary><c>POST /api/scan/line/complete</c></summary>
    public const string LineScanComplete = "LineScanComplete";

    /// <summary><c>POST /api/scan/line/abort</c></summary>
    public const string LineScanAbort = "LineScanAbort";

    /// <summary><c>POST /api/scan/surface</c></summary>
    public const string ScanSurface = "ScanSurface";

    /// <summary><c>POST /api/scan/surface-from-file</c></summary>
    public const string ScanSurfaceFromFile = "ScanSurfaceFromFile";

    /// <summary><c>POST /api/scan/hole</c></summary>
    public const string ScanHole = "ScanHole";

    /// <summary><c>GET /api/scan/surface/{taskId}/attachments</c></summary>
    public const string SurfaceAttachments = "SurfaceAttachments";
}
