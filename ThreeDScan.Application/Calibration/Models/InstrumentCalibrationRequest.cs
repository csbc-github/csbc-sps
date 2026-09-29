using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Calibration.Models;

/// <summary>POST /api/calibration/instrument 請求（API 串接規格 UC-01）。</summary>
public sealed class InstrumentCalibrationRequest
{
    /// <summary>面掃描器識別碼。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";

    /// <summary>拍攝群組 Scan 編號（1～16）；省略時用 Scanner 預設。不影響 Ref（ScannerConfig 每台一筆）。</summary>
    public int? NavigatorScanNo { get; set; }

    /// <summary>掃描器預期絕對位置（台船移動到位後的目標座標）。</summary>
    [Required]
    public CoordinateDto CameraExpectedPosition { get; set; } = new();

    /// <summary>標準球已知絕對座標；數量 1～10，以台船傳入為準。</summary>
    [Required, MinLength(1), MaxLength(10)]
    public List<StandardBallDto> StandardBalls { get; set; } = new();

    /// <summary>操作者（選填，寫入稽核）。</summary>
    public string? Operator { get; set; }

    /// <summary>
    /// 兩次拍攝任務 ID。第一次省略；第二次必填（帶第一次回傳的 taskId）。
    /// </summary>
    [StringLength(64)]
    public string? TaskId { get; set; }

    /// <summary>
    /// 是否為最後一次拍攝。第一次固定 <c>false</c>；第二次固定 <c>true</c>。
    /// 對應規格欄位 <c>final</c>。
    /// </summary>
    [JsonPropertyName("final")]
    public bool Final { get; set; }
}
