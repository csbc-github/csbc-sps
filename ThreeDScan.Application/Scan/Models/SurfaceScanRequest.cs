using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ThreeDScan.Application.Calibration.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>面掃描請求。</summary>
public sealed class SurfaceScanRequest : IValidatableObject
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

    /// <summary>是否量測兩翼厚度；預設 false。</summary>
    public bool MeasureThickness { get; set; }

    /// <summary><c>measureThickness=true</c> 時必填：夾具量測點 A。</summary>
    public CoordinateDto? ThicknessPointA { get; set; }

    /// <summary><c>measureThickness=true</c> 時必填：夾具量測點 B。</summary>
    public CoordinateDto? ThicknessPointB { get; set; }

    /// <summary>
    /// 當 <see cref="MeasureThickness"/> 為 true 時，檢查夾具點 A／B 皆有傳入。
    /// </summary>
    /// <param name="validationContext">ASP.NET 模型驗證內容。</param>
    /// <returns>缺 A 或 B 時的驗證錯誤；未量測則無錯誤。</returns>
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
