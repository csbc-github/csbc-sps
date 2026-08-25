using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ThreeDScan.Application.Calibration.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>面掃描請求；可分段呼叫，最後一次完成拼接。</summary>
public sealed class SurfaceScanRequest : IValidatableObject
{
    /// <summary>掃描器編號（與工作區固定對應）。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";

    /// <summary>第 2 次以後必填；第一次省略。</summary>
    [StringLength(64)]
    public string? TaskId { get; set; }

    /// <summary>是否為最後一次呼叫。</summary>
    [JsonPropertyName("final")]
    public bool Final { get; set; }

    /// <summary>是否執行厚度量測（僅噴印區使用）；預設 false。</summary>
    public bool MeasureThickness { get; set; }

    /// <summary><c>measureThickness=true</c> 時必填：夾具量測點 A。</summary>
    public CoordinateDto? ThicknessPointA { get; set; }

    /// <summary><c>measureThickness=true</c> 時必填：夾具量測點 B。</summary>
    public CoordinateDto? ThicknessPointB { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!MeasureThickness)
            yield break;

        if (ThicknessPointA is null)
        {
            yield return new ValidationResult(
                "measureThickness=true 時 thicknessPointA 必填。",
                [nameof(ThicknessPointA)]);
        }

        if (ThicknessPointB is null)
        {
            yield return new ValidationResult(
                "measureThickness=true 時 thicknessPointB 必填。",
                [nameof(ThicknessPointB)]);
        }
    }
}
