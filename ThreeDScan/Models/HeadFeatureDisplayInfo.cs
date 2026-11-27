using System.IO;
using System.Globalization;
using System.Text.Json;
using PlyProcessor.Core;
using ThreeDScan.Common;

namespace ThreeDScan.Models;

/// <summary>
/// 噴印頁右側：前緣三點、厚度、包角線 Z 差，以及與 2D PNG 相同的軸對齊包圍盒尺寸（mm）。
/// </summary>
public class HeadFeatureDisplayInfo : ViewModelBase
{
    private bool _hasData;
    private bool _hasHeadPoints;
    private string _sourceLabel = "";
    private string _sourceDescription = "";
    private double? _modelThicknessMm;
    private string _kind = "";
    private CoordinateInfo _leftCorner = new();
    private CoordinateInfo _apex = new();
    private CoordinateInfo _rightCorner = new();
    private double? _sizeX;
    private double? _sizeY;
    private double? _sizeZ;
    private double? _thicknessLeft;
    private double? _thicknessRight;
    private double? _ridgeZDelta;

    public bool HasData
    {
        get
        {
            return _hasData;
        }
        private set
        {
            SetProperty(ref _hasData, value);
        }
    }

    public bool HasHeadPoints { get => _hasHeadPoints; private set => SetProperty(ref _hasHeadPoints, value); }
    public string SourceLabel { get => _sourceLabel; private set => SetProperty(ref _sourceLabel, value); }
    public string SourceDescription { get => _sourceDescription; private set => SetProperty(ref _sourceDescription, value); }
    /// <summary>任務採用的建模板厚，不能當成左右翼的量測厚度。</summary>
    public double? ModelThicknessMm { get => _modelThicknessMm; private set => SetProperty(ref _modelThicknessMm, value); }

    public string Kind
    {
        get
        {
            return _kind;
        }
        private set
        {
            SetProperty(ref _kind, value);
        }
    }

    public CoordinateInfo LeftCorner
    {
        get
        {
            return _leftCorner;
        }
        private set
        {
            SetProperty(ref _leftCorner, value);
        }
    }

    public CoordinateInfo Apex
    {
        get
        {
            return _apex;
        }
        private set
        {
            SetProperty(ref _apex, value);
        }
    }

    public CoordinateInfo RightCorner
    {
        get
        {
            return _rightCorner;
        }
        private set
        {
            SetProperty(ref _rightCorner, value);
        }
    }

    /// <summary>全雲 maxX−minX，與俯視／正視 PNG 的 X 相同。</summary>
    public double? SizeX
    {
        get
        {
            return _sizeX;
        }
        private set
        {
            SetProperty(ref _sizeX, value);
        }
    }

    /// <summary>全雲 maxY−minY，與俯視／側視 PNG 的 Y 相同。</summary>
    public double? SizeY
    {
        get
        {
            return _sizeY;
        }
        private set
        {
            SetProperty(ref _sizeY, value);
        }
    }

    /// <summary>全雲 maxZ−minZ，與正視／側視 PNG 的 Z 相同。</summary>
    public double? SizeZ
    {
        get
        {
            return _sizeZ;
        }
        private set
        {
            SetProperty(ref _sizeZ, value);
        }
    }

    public void Clear()
    {
        HasHeadPoints = false;
        HasData = false;
        SourceLabel = "";
        SourceDescription = "";
        ModelThicknessMm = null;
        Kind = "";
        LeftCorner = new CoordinateInfo();
        Apex = new CoordinateInfo();
        RightCorner = new CoordinateInfo();
        SizeX = null;
        SizeY = null;
        SizeZ = null;
        ThicknessLeft = null;
        ThicknessRight = null;
        RidgeZDelta = null;
    }

    /// <summary>API artifact 優先讀 task.json 保存量測；尺寸取同 stem PLY 的完整包圍盒。</summary>
    public void LoadFromGlb(string glbPath)
    {
        ApplyArtifactData(ReadArtifact(glbPath));
    }

    public void LoadFromPly(string plyPath)
    {
        ApplyArtifactData(ReadArtifact(plyPath));
    }

    /// <summary>只讀小型 task.json；不讀點雲、不重新擬合已保存的 API 量測。</summary>
    public static ArtifactFeatureData ReadTaskMetadataForArtifact(string artifactPath)
    {
        if (string.IsNullOrWhiteSpace(artifactPath)) return new();
        var directory = Path.GetDirectoryName(Path.GetFullPath(artifactPath));
        string? taskDirectory = null;
        for (int i = 0; directory is not null && i < 8; i++, directory = Path.GetDirectoryName(directory))
        {
            if (Path.GetFileName(directory).StartsWith("SURFACE-", StringComparison.OrdinalIgnoreCase))
            {
                taskDirectory = directory;
                break;
            }
        }
        if (taskDirectory is null) return new();
        var data = new ArtifactFeatureData
        {
            IsApiTask = true, SourceTaskId = Path.GetFileName(taskDirectory),
            SourceLabel = "尚無可用的已保存前緣量測", SourceDescription = "來源：面掃描任務 " + Path.GetFileName(taskDirectory),
        };
        string taskPath = Path.Combine(taskDirectory, "task.json");
        try
        {
            if (!File.Exists(taskPath) || new FileInfo(taskPath).Length > 4 * 1024 * 1024) return data;
            using var json = JsonDocument.Parse(File.ReadAllText(taskPath));
            var root = json.RootElement;
            if (!root.TryGetProperty("kind", out var taskKind) || taskKind.GetString() != "Surface") return data;
            if (!root.TryGetProperty("taskId", out var taskId)
                || !string.Equals(taskId.GetString(), data.SourceTaskId, StringComparison.OrdinalIgnoreCase)) return data;
            double? modelThickness = TryReadOptionalDouble(root, "materialThicknessMm");
            if (!root.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Array)
                return data with { ModelThicknessMm = modelThickness };
            int count = segments.GetArrayLength();
            string merge = root.TryGetProperty("stitchMode", out var mode) && mode.GetString() == "NominalButtJoin"
                ? "依公稱間隙對接" : "依相機位姿合併";
            data = data with { SegmentCount = count, ModelThicknessMm = modelThickness,
                SourceLabel = $"第1段前緣／共{count}段／{merge}" };
            if (count == 0 || !segments[0].TryGetProperty("response", out var response)) return data;
            string frame = response.TryGetProperty("coordinateFrame", out var frameValue) ? frameValue.GetString() ?? "" : "";
            bool calibrated = response.TryGetProperty("absoluteCoordinatesCalibrated", out var calibratedValue)
                && calibratedValue.ValueKind == JsonValueKind.True;
            data = data with
            {
                ThicknessLeft = TryReadOptionalDouble(response, "thicknessLeft"),
                ThicknessRight = TryReadOptionalDouble(response, "thicknessRight"),
                RidgeZDelta = TryReadOptionalDouble(response, "segmentZDelta"),
                SourceDescription = $"來源：{data.SourceTaskId}／第1段保存量測；{frame}，XYZ mm"
                    + (calibrated ? "" : "（尚未經現場校正）"),
            };
            if (response.TryGetProperty("headFeatures", out var heads)
                && TryReadCoord(heads, "leftCorner", out var left) && TryReadCoord(heads, "apex", out var apex)
                && TryReadCoord(heads, "rightCorner", out var right))
                data = data with { HasHeadPoints = true, LeftCorner = left, Apex = apex, RightCorner = right, Kind = "Saved API measurement" };
            return data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return data;
        }
    }

    /// <summary>背景讀取：API 特徵取保存量測，包圍盒則遍歷實際同 stem PLY 的全部有限頂點。</summary>
    public static ArtifactFeatureData ReadArtifact(string artifactPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = ReadTaskMetadataForArtifact(artifactPath);
        if (string.IsNullOrWhiteSpace(artifactPath)) return data;
        string ply = Path.ChangeExtension(artifactPath, ".ply");
        if (!data.IsApiTask)
        {
            string? directory = Path.GetDirectoryName(artifactPath);
            string sidecar = Path.Combine(directory ?? "", "angle_steel_head_features.json");
            if (TryLoadFromJson(sidecar, out var left, out var apex, out var right, out var kind))
            {
                data = data with { HasHeadPoints = true, LeftCorner = left, Apex = apex, RightCorner = right, Kind = kind,
                    SourceLabel = "前緣特徵", SourceDescription = "來源：既有特徵檔" };
                using var json = JsonDocument.Parse(File.ReadAllText(sidecar));
                data = data with { ThicknessLeft = TryReadOptionalDouble(json.RootElement, "thicknessLeft"),
                    ThicknessRight = TryReadOptionalDouble(json.RootElement, "thicknessRight"),
                    RidgeZDelta = TryReadOptionalDouble(json.RootElement, "ridgeZDelta") };
            }
            else if (TryReadPlyPoints(ply, out var points))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extracted = HeadFeatureExtractor.Extract(points);
                if (extracted is not null)
                    data = data with { HasHeadPoints = true,
                        LeftCorner = new(extracted.LeftX, extracted.LeftY, extracted.LeftZ),
                        Apex = new(extracted.ApexX, extracted.ApexY, extracted.ApexZ),
                        RightCorner = new(extracted.RightX, extracted.RightY, extracted.RightZ), Kind = extracted.Kind,
                        RidgeZDelta = extracted.RidgeZDelta, SourceLabel = "前緣特徵", SourceDescription = "來源：舊版點雲特徵估計" };
            }
        }
        try { return ReadPlyBounds(ply, data, cancellationToken); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return data; }
    }

    public void ApplyArtifactData(ArtifactFeatureData data)
    {
        Clear();
        SourceLabel = data.SourceLabel;
        SourceDescription = data.SourceDescription;
        ModelThicknessMm = data.ModelThicknessMm;
        ThicknessLeft = data.ThicknessLeft;
        ThicknessRight = data.ThicknessRight;
        RidgeZDelta = data.RidgeZDelta;
        if (data.HasHeadPoints && data.LeftCorner is not null && data.Apex is not null && data.RightCorner is not null)
            ApplyPoints(data.LeftCorner, data.Apex, data.RightCorner, data.Kind);
        SizeX = data.SizeX; SizeY = data.SizeY; SizeZ = data.SizeZ;
        HasData = HasHeadPoints || SizeX.HasValue || SizeY.HasValue || SizeZ.HasValue || ModelThicknessMm.HasValue;
    }

    private static ArtifactFeatureData ReadPlyBounds(string path, ArtifactFeatureData data, CancellationToken token)
    {
        if (!File.Exists(path)) return data;
        bool header = true, vertexProperties = false, ascii = false;
        int count = 0, read = 0, property = 0, ix = -1, iy = -1, iz = -1;
        double minX = double.PositiveInfinity, minY = minX, minZ = minX;
        double maxX = double.NegativeInfinity, maxY = maxX, maxZ = maxX;
        foreach (string line in File.ReadLines(path))
        {
            if ((read & 255) == 0) token.ThrowIfCancellationRequested();
            if (header)
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (line.StartsWith("format ascii ", StringComparison.Ordinal)) ascii = true;
                if (fields.Length >= 3 && fields[0] == "element")
                {
                    vertexProperties = fields[1] == "vertex";
                    if (vertexProperties && !int.TryParse(fields[2], out count)) return data;
                }
                if (vertexProperties && fields.Length == 3 && fields[0] == "property")
                {
                    if (fields[2] == "x") ix = property;
                    if (fields[2] == "y") iy = property;
                    if (fields[2] == "z") iz = property;
                    property++;
                }
                if (line.Trim() == "end_header")
                {
                    if (!ascii || ix < 0 || iy < 0 || iz < 0) return data;
                    header = false;
                }
                continue;
            }
            if (read++ >= count) break;
            if (!ReadNumber(line, ix, out double x) || !ReadNumber(line, iy, out double y) || !ReadNumber(line, iz, out double z)) continue;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }
        return double.IsFinite(minX) ? data with { SizeX = maxX - minX, SizeY = maxY - minY, SizeZ = maxZ - minZ } : data;
    }

    private static bool ReadNumber(ReadOnlySpan<char> text, int column, out double value)
    {
        for (int i = 0; ; i++)
        {
            text = text.TrimStart();
            int length = 0;
            while (length < text.Length && !char.IsWhiteSpace(text[length])) length++;
            if (length == 0) { value = 0; return false; }
            if (i == column) return double.TryParse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
            text = text[length..];
        }
    }

    private void TryApplyExtract(PlyPoint[] pts)
    {
        var extracted = HeadFeatureExtractor.Extract(pts);
        if (extracted is null)
        {
            return;
        }

        ApplyPoints(
            new CoordinateInfo(extracted.LeftX, extracted.LeftY, extracted.LeftZ),
            new CoordinateInfo(extracted.ApexX, extracted.ApexY, extracted.ApexZ),
            new CoordinateInfo(extracted.RightX, extracted.RightY, extracted.RightZ),
            extracted.Kind);
        RidgeZDelta = extracted.RidgeZDelta;
    }

    private void ApplyPoints(
        CoordinateInfo left,
        CoordinateInfo apex,
        CoordinateInfo right,
        string kind)
    {
        LeftCorner = left;
        Apex = apex;
        RightCorner = right;
        Kind = kind ?? "";
        HasHeadPoints = true;
        HasData = true;
    }

    private void ApplyAabb(PlyPoint[] pts)
    {
        if (pts is null || pts.Length == 0)
        {
            return;
        }

        float minX = pts[0].X;
        float maxX = pts[0].X;
        float minY = pts[0].Y;
        float maxY = pts[0].Y;
        float minZ = pts[0].Z;
        float maxZ = pts[0].Z;
        for (int i = 1; i < pts.Length; i++)
        {
            var p = pts[i];
            if (p.X < minX)
            {
                minX = p.X;
            }

            if (p.X > maxX)
            {
                maxX = p.X;
            }

            if (p.Y < minY)
            {
                minY = p.Y;
            }

            if (p.Y > maxY)
            {
                maxY = p.Y;
            }

            if (p.Z < minZ)
            {
                minZ = p.Z;
            }

            if (p.Z > maxZ)
            {
                maxZ = p.Z;
            }
        }

        SizeX = maxX - minX;
        SizeY = maxY - minY;
        SizeZ = maxZ - minZ;
        HasData = true;
    }

    /// <summary>左側厚度（mm）；sidecar 沒有時為 null。</summary>
    public double? ThicknessLeft
    {
        get
        {
            return _thicknessLeft;
        }
        private set
        {
            SetProperty(ref _thicknessLeft, value);
        }
    }

    /// <summary>右側厚度（mm）；sidecar 沒有時為 null。</summary>
    public double? ThicknessRight
    {
        get
        {
            return _thicknessRight;
        }
        private set
        {
            SetProperty(ref _thicknessRight, value);
        }
    }

    /// <summary>本段包角線最大 Z 差（mm），對應 API segmentZDelta。</summary>
    public double? RidgeZDelta
    {
        get
        {
            return _ridgeZDelta;
        }
        private set
        {
            SetProperty(ref _ridgeZDelta, value);
        }
    }

    private void ApplyMeasuresFromJson(string jsonPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = doc.RootElement;
            ThicknessLeft = TryReadOptionalDouble(root, "thicknessLeft");
            ThicknessRight = TryReadOptionalDouble(root, "thicknessRight");
            RidgeZDelta = TryReadOptionalDouble(root, "ridgeZDelta");
        }
        catch (Exception)
        {
        }
    }

    private static double? TryReadOptionalDouble(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var value) && double.IsFinite(value))
        {
            return value;
        }

        return null;
    }

    private static bool TryLoadFromJson(
        string jsonPath,
        out CoordinateInfo left,
        out CoordinateInfo apex,
        out CoordinateInfo right,
        out string kind)
    {
        left = new CoordinateInfo();
        apex = new CoordinateInfo();
        right = new CoordinateInfo();
        kind = "";
        if (!File.Exists(jsonPath))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = doc.RootElement;
            if (!TryReadCoord(root, "leftCorner", out left)
                || !TryReadCoord(root, "apex", out apex)
                || !TryReadCoord(root, "rightCorner", out right))
            {
                return false;
            }

            if (root.TryGetProperty("kind", out var kindEl)
                && kindEl.ValueKind == JsonValueKind.String)
            {
                kind = kindEl.GetString() ?? "";
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryReadPlyPoints(string plyPath, out PlyPoint[] pts)
    {
        pts = Array.Empty<PlyPoint>();
        if (string.IsNullOrWhiteSpace(plyPath) || !File.Exists(plyPath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(plyPath);
            pts = PlyReader.Parse(stream).Points;
            if (pts is null || pts.Length == 0)
            {
                return false;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryReadCoord(JsonElement root, string name, out CoordinateInfo coord)
    {
        coord = new CoordinateInfo();
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!el.TryGetProperty("x", out var xEl)
            || !el.TryGetProperty("y", out var yEl)
            || !el.TryGetProperty("z", out var zEl))
        {
            return false;
        }

        if (!xEl.TryGetDouble(out var x)
            || !yEl.TryGetDouble(out var y)
            || !zEl.TryGetDouble(out var z))
        {
            return false;
        }

        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return false;
        coord = new CoordinateInfo(x, y, z);
        return true;
    }
}

/// <summary>背景讀取的獨立結果；由 UI dispatcher 一次套用，沒有 WPF 執行緒相依。</summary>
public sealed record ArtifactFeatureData
{
    public bool IsApiTask { get; init; }
    public bool HasHeadPoints { get; init; }
    public string SourceTaskId { get; init; } = "";
    public int SegmentCount { get; init; }
    public string SourceLabel { get; init; } = "";
    public string SourceDescription { get; init; } = "";
    public string Kind { get; init; } = "";
    public CoordinateInfo? LeftCorner { get; init; }
    public CoordinateInfo? Apex { get; init; }
    public CoordinateInfo? RightCorner { get; init; }
    public double? ThicknessLeft { get; init; }
    public double? ThicknessRight { get; init; }
    public double? RidgeZDelta { get; init; }
    public double? ModelThicknessMm { get; init; }
    public double? SizeX { get; init; }
    public double? SizeY { get; init; }
    public double? SizeZ { get; init; }
}
