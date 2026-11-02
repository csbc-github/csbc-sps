using System.Text.Json;

namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>抽取／拼接報告 JSON 選項。</summary>
/// <remarks>由 Hank Scan3D 的 ExtractionJson 搬入，邏輯不改。</remarks>
public static class ExtractionJson
{
    /// <summary>camelCase、不分大小寫、縮排。</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
