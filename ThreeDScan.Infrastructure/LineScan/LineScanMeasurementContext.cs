using System.Text.Json;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>Recorded calibration and streamed profiles, including nearby table support.</summary>
public sealed record LineScanMeasurementContext(
    Calibration Calibration, IEnumerable<IReadOnlyList<Vertex>> Frames)
{
    static readonly JsonSerializerOptions FrozenOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Returns the recorded calibration without consulting current database settings.</summary>
    public static Calibration? ReadCapturedCalibration(string sessionDirectory)
    {
        string capturePath = Path.Combine(Path.GetFullPath(sessionDirectory), "line_raw.capture.json");
        if (!File.Exists(capturePath)) return null;
        using var capture = ReadCapture(capturePath);
        return ReadCalibration(capture.RootElement);
    }

    public static LineScanMeasurementContext Load(
        string sessionDirectory, Calibration fallbackCalibration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.GetFullPath(sessionDirectory);
        string source = Path.Combine(directory, "line_raw.ply");
        if (!File.Exists(source))
            throw new InvalidDataException("缺少原始 line_raw.ply，不能由已裁切高度的 3D PLY 還原桌面接點。");
        using var capture = ReadCapture(Path.ChangeExtension(source, ".capture.json"));
        var root = capture.RootElement;
        var calibration = ReadCalibration(root) ?? fallbackCalibration;
        calibration.Validate();

        ReconstructionOptions settings;
        if (TryProperty(root, "reconstruction_settings", out var frozen))
        {
            settings = ReadSettings(frozen);
        }
        else
        {
            // Older sessions may keep the same frozen settings in the completed
            // reconstruction report rather than in the capture sidecar.
            string output = SickCSharpLineScanner.ResolveCompletedOutputPath(directory);
            string metadataPath = Path.ChangeExtension(output, ".json");
            if (!File.Exists(metadataPath))
                throw new InvalidDataException("缺少錄製時重建設定，無法確認平均、框選及 Encoder 比例。");
            using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
            if (!TryProperty(metadata.RootElement, "settings", out var saved))
                throw new InvalidDataException("重建報告缺少保存的量測設定。");
            settings = ReadSettings(saved);
        }
        settings = settings with { RoiXY = settings.RoiXY?.ToArray() };
        StreamingReconstruction.Validate(calibration, settings);
        return new(calibration, StreamingReconstruction.ReadMeasurementFrames(source, calibration, settings, cancellationToken));
    }

    static JsonDocument ReadCapture(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException("缺少 line_raw.capture.json 錄製設定。");
        var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new InvalidDataException("錄製設定必須是 JSON 物件。");
        }
        if (TryProperty(root, "error", out var error) && error.ValueKind is not (JsonValueKind.Null or JsonValueKind.False)
            && !(error.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(error.GetString()))
            || TryProperty(root, "complete", out var complete) && complete.ValueKind == JsonValueKind.False)
        {
            document.Dispose();
            throw new InvalidDataException("原始錄製未完整完成，不能量測。");
        }
        return document;
    }

    static Calibration? ReadCalibration(JsonElement root)
    {
        if (!TryProperty(root, "calibration", out var calibration)) return null;
        P ReadPoint(string name)
        {
            if (!TryProperty(calibration, name, out var point) || point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2)
                throw new InvalidDataException("錄製設定的桌面校正點無效。");
            return new(point[0].GetDouble(), point[1].GetDouble());
        }
        var result = new Calibration(ReadPoint("first"), ReadPoint("second"));
        result.Validate();
        return result;
    }

    static ReconstructionOptions ReadSettings(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object
            || !TryProperty(settings, "average_scans", out _)
            || !TryProperty(settings, "position_source", out _))
            throw new InvalidDataException("錄製設定缺少平均圈數或行程來源。");
        var names = typeof(ReconstructionOptions).GetProperties().ToDictionary(
            p => Normalize(p.Name), p => JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name));
        var normalized = new Dictionary<string, JsonElement>();
        foreach (var property in settings.EnumerateObject())
            if (names.TryGetValue(Normalize(property.Name), out var name)) normalized[name] = property.Value;
        return JsonSerializer.Deserialize<ReconstructionOptions>(JsonSerializer.Serialize(normalized), FrozenOptions)
            ?? throw new InvalidDataException("錄製時重建設定無效。");
    }

    static bool TryProperty(JsonElement value, string name, out JsonElement result)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
                if (Normalize(property.Name) == Normalize(name)) { result = property.Value; return true; }
        result = default;
        return false;
    }

    static string Normalize(string name) => name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
}
