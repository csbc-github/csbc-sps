using System.Text.Json;
using System.Globalization;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Scan;

/// <summary>
/// 解析 ContentsRoot/_monitor/latest-hole-boundary.json（<c>featureBoundary</c>、<c>rawPlyPath</c>）。
/// </summary>
public sealed class HoleBoundaryReader : IHoleBoundaryReader
{
    /// <inheritdoc />
    public HoleBoundarySnapshot? TryRead(string contentsRoot)
    {
        var path = ScanMonitorPaths.HoleBoundaryFile(contentsRoot);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            bool hasBoundary = TryGet(root, "featureBoundary", out var boundary);
            bool hasFeatures = TryGet(root, "features", out var featuresElement);
            if (!hasBoundary && !hasFeatures)
            {
                return null;
            }

            var features = new List<HoleFeatureSnapshot>();
            if (hasFeatures)
            {
                if (featuresElement.ValueKind != JsonValueKind.Array) throw new JsonException("features must be an array.");
                foreach (var item in featuresElement.EnumerateArray())
                {
                    string status = ReadString(item, "status") ?? "PARTIAL";
                    bool closed = status.Equals("CLOSED", StringComparison.OrdinalIgnoreCase)
                        && (!TryGet(item, "closed", out var closedElement) || closedElement.ValueKind == JsonValueKind.True);
                    features.Add(new HoleFeatureSnapshot
                    {
                        Id = ReadString(item, "id") ?? "",
                        FaceId = ReadString(item, "faceId") ?? "",
                        Type = ReadString(item, "type") ?? "Unknown",
                        Status = closed ? "CLOSED" : "PARTIAL",
                        Closed = closed,
                        WidthMm = ReadOptionalNumber(item, "widthMm"),
                        HeightMm = ReadOptionalNumber(item, "heightMm"),
                        Reason = ReadString(item, "reason"),
                        Boundary = TryGet(item, "boundary", out var publicBoundary) ? ReadPoints(publicBoundary) : [],
                        ScannerBoundary = TryGet(item, "scannerBoundary", out var scannerBoundary) ? ReadPoints(scannerBoundary) : [],
                    });
                }
            }

            return new HoleBoundarySnapshot
            {
                Points = hasBoundary ? ReadPoints(boundary) : [],
                RawPlyPath = ReadString(root, "rawPlyPath")?.Trim(),
                ExtractedPlyPath = ReadString(root, "extractedPlyPath")?.Trim(),
                Features = features,
                SelectedFeatureId = ReadString(root, "selectedFeatureId"),
                TaskId = ReadString(root, "taskId"),
                CapturedAt = DateTimeOffset.TryParse(ReadString(root, "capturedAt"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var capturedAt) ? capturedAt : null,
                CoordinateFrame = ReadString(root, "coordinateFrame"),
                AngleSteelOnly = TryGet(root, "holeScope", out var scope)
                    && scope.ValueKind == JsonValueKind.Object && ReadString(scope, "mode") == "AngleSteelOnly",
                WorkpieceIdentified = scope.ValueKind == JsonValueKind.Object
                    && TryGet(scope, "workpieceIdentified", out var identified) && identified.ValueKind == JsonValueKind.True,
                ScopeReason = scope.ValueKind == JsonValueKind.Object ? ReadString(scope, "reason") : null,
                AbsoluteCoordinatesCalibrated = TryGet(root, "absoluteCoordinatesCalibrated", out var calibrated)
                    && calibrated.ValueKind is JsonValueKind.True or JsonValueKind.False ? calibrated.GetBoolean() : null,
                Warnings = TryGet(root, "warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array
                    ? warnings.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
                    : [],
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<CoordinateDto> ReadPoints(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array) throw new JsonException("A measured boundary must be an array.");
        var points = new List<CoordinateDto>();
        foreach (var point in array.EnumerateArray())
        {
            points.Add(new CoordinateDto
            {
                X = ReadOptionalNumber(point, "x") ?? throw new JsonException("Measured X is missing."),
                Y = ReadOptionalNumber(point, "y") ?? throw new JsonException("Measured Y is missing."),
                Z = ReadOptionalNumber(point, "z") ?? throw new JsonException("Measured Z is missing."),
            });
        }
        return points;
    }

    private static double? ReadOptionalNumber(JsonElement element, string name)
    {
        if (TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double number) && double.IsFinite(number)) return number;
        return null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }
        return element.TryGetProperty(name, out value)
            || element.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value);
    }
}
