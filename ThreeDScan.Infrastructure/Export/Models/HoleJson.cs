using System.Text.Json;

namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>孔洞 JSON 選項。JS 使用 uAxis / xMm，不用預設縮寫轉換。</summary>
/// <remarks>由 Hank Scan3D 的 HoleJson 搬入，邏輯不改。</remarks>
public static class HoleJson
{
    /// <summary>首字母小寫、不分大小寫、縮排。</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = new HoleNamingPolicy(),
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private sealed class HoleNamingPolicy : JsonNamingPolicy
    {
        /// <inheritdoc />
        public override string ConvertName(string name)
        {
            return char.ToLowerInvariant(name[0]) + name[1..];
        }
    }
}
