using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThreeDScan.Api.Common;

/// <summary>DAS 對外 JSON（GET 回應與 callback 本體）共用序列化規則。</summary>
internal static class DasApiJson
{
    /// <summary>callback POST 與 API 控制器共用選項。</summary>
    public static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    /// <summary>將相同規則套用至 MVC <c>AddJsonOptions</c>。</summary>
    /// <param name="options">ASP.NET Core 注入的 JSON 選項。</param>
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }
}