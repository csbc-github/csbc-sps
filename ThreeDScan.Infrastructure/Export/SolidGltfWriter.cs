using System.Text;
using System.Text.Json;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>寫出 glTF／GLB。</summary>
/// <remarks>由 Hank Scan3D 的 SolidGltfWriter 搬入，邏輯不改。</remarks>
internal static class SolidGltfWriter
{
    /// <summary>寫出 glTF 與 GLB。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.SolidGltfWriter</c>。</remarks>
    /// <param name="gltfPath">glTF 路徑。</param>
    /// <param name="glbPath">GLB 路徑。</param>
    /// <param name="model">實體。</param>
    internal static void Write(string gltfPath, string glbPath, SolidModel model)
    {
        var normals = new ScanVector[model.Vertices.Count];
        foreach (var t in model.Triangles)
        {
            var n = (model.Vertices[t.B] - model.Vertices[t.A]).Cross(model.Vertices[t.C] - model.Vertices[t.A]);
            normals[t.A] += n;
            normals[t.B] += n;
            normals[t.C] += n;
        }

        // glTF uses metres and Y-up. This proper rotation maps scan (x,y,z) to (x,z,-y).
        static ScanVector Rotate(ScanVector p)
        {
            return new ScanVector(p.X, p.Z, -p.Y);
        }

        var positions = model.Vertices.Select(p => Rotate(p).Scale(.001)).ToArray();
        using var stream = new MemoryStream();
        using var binary = new BinaryWriter(stream, Encoding.UTF8, true);
        void Vector(ScanVector p)
        {
            binary.Write((float)p.X);
            binary.Write((float)p.Y);
            binary.Write((float)p.Z);
        }

        foreach (var p in positions)
        {
            Vector(p);
        }

        int normalOffset = (int)stream.Position;
        foreach (var n in normals)
        {
            Vector(Rotate(n.Normalize()));
        }

        int indexOffset = (int)stream.Position;
        foreach (var t in model.Triangles)
        {
            binary.Write((uint)t.A);
            binary.Write((uint)t.B);
            binary.Write((uint)t.C);
        }

        byte[] buffer = stream.ToArray();
        var bounds = ScanBounds.Of(positions);
        object Document(string? uri)
        {
            return new
            {
                asset = new { version = "2.0", generator = "Scan3D.Processing.Legacy.Stitching C# solid exporter" },
                scene = 0,
                scenes = new[] { new { nodes = new[] { 0 } } },
                nodes = new[] { new { mesh = 0, name = "Stitched angle steel" } },
                meshes = new[] { new { name = "Solid", primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1 }, indices = 2, material = 0, mode = 4 } } } },
                materials = new[] { new { name = "Steel", pbrMetallicRoughness = new { baseColorFactor = new[] { .30, .54, .59, 1.0 }, metallicFactor = .65, roughnessFactor = .45 } } },
                buffers = new[] { new { uri, byteLength = buffer.Length } },
                bufferViews = new[]
                {
                    new { buffer = 0, byteOffset = 0, byteLength = normalOffset, target = 34962 },
                    new { buffer = 0, byteOffset = normalOffset, byteLength = indexOffset - normalOffset, target = 34962 },
                    new { buffer = 0, byteOffset = indexOffset, byteLength = buffer.Length - indexOffset, target = 34963 },
                },
                accessors = new object[]
                {
                    new
                    {
                        bufferView = 0, componentType = 5126, count = positions.Length, type = "VEC3",
                        min = new[] { (float)bounds.MinX, (float)bounds.MinY, (float)bounds.MinZ },
                        max = new[] { (float)bounds.MaxX, (float)bounds.MaxY, (float)bounds.MaxZ },
                    },
                    new { bufferView = 1, componentType = 5126, count = positions.Length, type = "VEC3" },
                    new { bufferView = 2, componentType = 5125, count = model.Triangles.Count * 3, type = "SCALAR" },
                },
                extras = new
                {
                    thicknessMm = model.Options.ThicknessMm,
                    meshCellMm = model.Options.CellMm,
                    scanMillimetresToGltfMetres = "(x,y,z) -> (0.001*x,0.001*z,-0.001*y)",
                    reconstructed = true,
                },
            };
        }

        var jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        File.WriteAllText(gltfPath, JsonSerializer.Serialize(Document("data:application/octet-stream;base64," + Convert.ToBase64String(buffer)), jsonOptions), new UTF8Encoding(false));
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(Document(null), jsonOptions);
        int jsonLength = (json.Length + 3) & ~3, binLength = (buffer.Length + 3) & ~3;
        using var glb = new BinaryWriter(File.Create(glbPath));
        glb.Write(0x46546c67u);
        glb.Write(2u);
        glb.Write((uint)(12 + 8 + jsonLength + 8 + binLength));
        glb.Write((uint)jsonLength);
        glb.Write(0x4e4f534au);
        glb.Write(json);
        for (int i = json.Length; i < jsonLength; i++)
        {
            glb.Write((byte)32);
        }

        glb.Write((uint)binLength);
        glb.Write(0x004e4942u);
        glb.Write(buffer);
        for (int i = buffer.Length; i < binLength; i++)
        {
            glb.Write((byte)0);
        }
    }
}
