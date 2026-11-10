using System.Text.Json;

namespace ThreeDScan.Infrastructure.LineScan.Hank;

internal static class ScanJson
{
    // Raw capture and reconstruction metadata retain the original scanner JSON format.
    // The public DAS API uses the separate camelCase ScanApiOptions.Json settings.
    public static readonly JsonSerializerOptions Options = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
