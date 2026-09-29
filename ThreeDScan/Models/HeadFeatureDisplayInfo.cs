using System.IO;
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
        HasData = false;
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

    /// <summary>三點優先 sidecar JSON；XYZ 尺寸一律用同目錄 PLY 包圍盒（與 PNG 同一批點）。</summary>
    public void LoadFromGlb(string glbPath)
    {
        Clear();
        if (string.IsNullOrWhiteSpace(glbPath))
        {
            return;
        }

        var dir = Path.GetDirectoryName(glbPath);
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        var jsonPath = Path.Combine(dir, "angle_steel_head_features.json");
        var plyPath = Path.Combine(dir, "angle_steel.ply");
        if (TryLoadFromJson(jsonPath, out var left, out var apex, out var right, out var kind))
        {
            ApplyPoints(left, apex, right, kind);
            ApplyMeasuresFromJson(jsonPath);
        }

        if (!TryReadPlyPoints(plyPath, out var pts))
        {
            return;
        }

        ApplyAabb(pts);
        if (!HasData)
        {
            TryApplyExtract(pts);
        }
    }

    public void LoadFromPly(string plyPath)
    {
        Clear();
        if (!TryReadPlyPoints(plyPath, out var pts))
        {
            return;
        }

        ApplyAabb(pts);
        TryApplyExtract(pts);
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

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var value))
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

        coord = new CoordinateInfo(x, y, z);
        return true;
    }
}
