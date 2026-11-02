using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThreeDScan.Domain.Models;

/// <summary>任務文件 JSON 預設。對應 Hank <c>Scan3D.Service.JsonDefaults</c>。</summary>
public static class JsonDefaults
{
    /// <summary>Web 預設、縮排、列舉字串。</summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>物件轉 JsonElement。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.JsonDefaults</c>。</remarks>
    /// <param name="value">來源。</param>
    /// <returns>序列化後的元素。</returns>
    public static JsonElement Element(object value)
    {
        return JsonSerializer.SerializeToElement(value, Options);
    }

    /// <summary>建立序列化選項。</summary>
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            RespectRequiredConstructorParameters = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
