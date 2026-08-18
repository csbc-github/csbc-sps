using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ThreeDScan.Controls;

/// <summary>
/// 讀取 PlyProcessor GltfExporter 產出的簡易 GLB
///（POSITION + NORMAL + indices，單一 mesh）。
/// </summary>
public static class GlbMeshLoader
{
    public sealed record MeshData(
        Point3DCollection Positions,
        Vector3DCollection Normals,
        Int32Collection TriangleIndices,
        Rect3D Bounds);

    public static MeshData LoadFromFile(string glbPath)
    {
        var bytes = File.ReadAllBytes(glbPath);
        return LoadFromBytes(bytes);
    }

    public static MeshData LoadFromBytes(byte[] glb)
    {
        if (glb.Length < 20)
            throw new InvalidDataException("GLB 太短。");

        var magic = BitConverter.ToUInt32(glb, 0);
        if (magic != 0x46546C67u)
            throw new InvalidDataException("不是有效的 GLB（magic 不符）。");

        var version = BitConverter.ToUInt32(glb, 4);
        if (version != 2)
            throw new InvalidDataException($"不支援 GLB version {version}。");

        var offset = 12;
        byte[]? jsonChunk = null;
        byte[]? binChunk = null;

        while (offset + 8 <= glb.Length)
        {
            var chunkLen = (int)BitConverter.ToUInt32(glb, offset);
            var chunkType = BitConverter.ToUInt32(glb, offset + 4);
            offset += 8;
            if (offset + chunkLen > glb.Length)
                throw new InvalidDataException("GLB chunk 長度超出檔案。");

            var chunk = new byte[chunkLen];
            Buffer.BlockCopy(glb, offset, chunk, 0, chunkLen);
            offset += chunkLen;

            if (chunkType == 0x4E4F534Au) // JSON
                jsonChunk = chunk;
            else if (chunkType == 0x004E4942u) // BIN
                binChunk = chunk;
        }

        if (jsonChunk is null || binChunk is null)
            throw new InvalidDataException("GLB 缺少 JSON 或 BIN chunk。");

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(jsonChunk).TrimEnd('\0', ' '));
        var root = doc.RootElement;
        var accessors = root.GetProperty("accessors");
        var bufferViews = root.GetProperty("bufferViews");
        var meshes = root.GetProperty("meshes");
        var prim = meshes[0].GetProperty("primitives")[0];
        var attrs = prim.GetProperty("attributes");

        var posAcc = attrs.GetProperty("POSITION").GetInt32();
        var norAcc = attrs.GetProperty("NORMAL").GetInt32();
        var idxAcc = prim.GetProperty("indices").GetInt32();

        var positions = ReadVec3(accessors[posAcc], bufferViews, binChunk);
        var normals = ReadVec3AsNormals(accessors[norAcc], bufferViews, binChunk);
        var indices = ReadIndices(accessors[idxAcc], bufferViews, binChunk);

        var min = accessors[posAcc].GetProperty("min");
        var max = accessors[posAcc].GetProperty("max");
        var minX = min[0].GetDouble();
        var minY = min[1].GetDouble();
        var minZ = min[2].GetDouble();
        var maxX = max[0].GetDouble();
        var maxY = max[1].GetDouble();
        var maxZ = max[2].GetDouble();
        var bounds = new Rect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);

        return new MeshData(positions, normals, indices, bounds);
    }

    /// <summary>從 Pipeline ZIP bytes 取出第一個 .glb（供測試用）。</summary>
    public static byte[]? TryExtractFirstGlbFromZip(byte[] zipBytes)
    {
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.Name.EndsWith(".glb", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;
        using var stream = entry.Open();
        using var outMs = new MemoryStream();
        stream.CopyTo(outMs);
        return outMs.ToArray();
    }

    static Point3DCollection ReadVec3(JsonElement accessor, JsonElement bufferViews, byte[] bin)
    {
        var (view, byteOffset, count) = ResolveAccessor(accessor, bufferViews);
        var start = view.GetProperty("byteOffset").GetInt32() + byteOffset;
        var pts = new Point3DCollection(count);
        for (var i = 0; i < count; i++)
        {
            var o = start + i * 12;
            pts.Add(new Point3D(
                BitConverter.ToSingle(bin, o),
                BitConverter.ToSingle(bin, o + 4),
                BitConverter.ToSingle(bin, o + 8)));
        }

        return pts;
    }

    static Vector3DCollection ReadVec3AsNormals(JsonElement accessor, JsonElement bufferViews, byte[] bin)
    {
        var (view, byteOffset, count) = ResolveAccessor(accessor, bufferViews);
        var start = view.GetProperty("byteOffset").GetInt32() + byteOffset;
        var norms = new Vector3DCollection(count);
        for (var i = 0; i < count; i++)
        {
            var o = start + i * 12;
            norms.Add(new Vector3D(
                BitConverter.ToSingle(bin, o),
                BitConverter.ToSingle(bin, o + 4),
                BitConverter.ToSingle(bin, o + 8)));
        }

        return norms;
    }

    static Int32Collection ReadIndices(JsonElement accessor, JsonElement bufferViews, byte[] bin)
    {
        var (view, byteOffset, count) = ResolveAccessor(accessor, bufferViews);
        var start = view.GetProperty("byteOffset").GetInt32() + byteOffset;
        var componentType = accessor.GetProperty("componentType").GetInt32();
        var indices = new Int32Collection(count);
        if (componentType == 5125) // UNSIGNED_INT
        {
            for (var i = 0; i < count; i++)
                indices.Add(unchecked((int)BitConverter.ToUInt32(bin, start + i * 4)));
        }
        else if (componentType == 5123) // UNSIGNED_SHORT
        {
            for (var i = 0; i < count; i++)
                indices.Add(BitConverter.ToUInt16(bin, start + i * 2));
        }
        else
        {
            throw new InvalidDataException($"不支援的 index componentType={componentType}");
        }

        return indices;
    }

    static (JsonElement View, int ByteOffset, int Count) ResolveAccessor(JsonElement accessor, JsonElement bufferViews)
    {
        var viewIndex = accessor.GetProperty("bufferView").GetInt32();
        var byteOffset = accessor.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0;
        var count = accessor.GetProperty("count").GetInt32();
        return (bufferViews[viewIndex], byteOffset, count);
    }
}
