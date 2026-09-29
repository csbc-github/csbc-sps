using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>
/// 略過掃描器擷取，用既有資料夾的 raw_scene.ply 跑與面掃描相同的後續處理。
/// </summary>
public sealed class SurfaceFromFileRequest
{
    /// <summary>掃描器編號（與工作區固定對應）。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";

    /// <summary>工單號。</summary>
    [Required, StringLength(64, MinimumLength = 1)]
    public string JobId { get; set; } = "";

    /// <summary>第 2 次以後必填；第一次省略。</summary>
    [StringLength(64)]
    public string? TaskId { get; set; }

    /// <summary>是否為最後一次呼叫。</summary>
    [JsonPropertyName("final")]
    public bool Final { get; set; }

    /// <summary>本次 DAS 期望掃描器到位的機台座標 (mm)。公開點雲 = 相對 + 此值 + UC-01 補償。</summary>
    [Required]
    public CoordinateDto? CameraPosition { get; set; }

    /// <summary>左斜面內表面座標（測距儀，與公開點雲同一絕對座標 mm）。</summary>
    public CoordinateDto? InnerLeft { get; set; }

    /// <summary>右斜面內表面座標（測距儀，與公開點雲同一絕對座標 mm）。</summary>
    public CoordinateDto? InnerRight { get; set; }

    /// <summary>
    /// 含 <c>raw_scene.ply</c> 的資料夾。空白則用該掃描器 <c>OfflineCaptureDirectory</c>。
    /// </summary>
    public string? CaptureDirectory { get; set; }

    /// <summary>轉成面掃描請求，供後續與 <c>POST /api/scan/surface</c> 同一條處理。</summary>
    /// <returns>不含擷取來源路徑的面掃描請求。</returns>
    public SurfaceScanRequest ToSurfaceScanRequest()
    {
        return new SurfaceScanRequest
        {
            ScannerId = ScannerId,
            JobId = JobId,
            TaskId = TaskId,
            Final = Final,
            CameraPosition = CameraPosition,
            InnerLeft = InnerLeft,
            InnerRight = InnerRight,
        };
    }
}
