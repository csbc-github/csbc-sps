using System.IO.Compression;

namespace PlyProcessor.Core;

public record PipelineOptions(
    // ── Scan processing (replaces Python backend) ─────────────────────────
    float ZThresh       = 12f,    // minimum height above ground plane to keep (mm)
    float NoiseVoxel    = 3f,     // voxel size for initial noise-cluster removal
    int   MinNoiseSize  = 200,    // clusters smaller than this are discarded as noise
    // ── Object clustering ─────────────────────────────────────────────────
    float VoxelSize     = 10f,    // voxel size for object-level clustering
    int   MinClusterSize= 200,
    float PlaneTol      = 8f,
    bool  RemoveDominantPlane = false,
    // ── Mesh / export ─────────────────────────────────────────────────────
    float MeshCellSize  = 2f,
    float MeshThickness = 6f,
    int   SmoothIter    = 4,
    int   ErodeN        = 1,
    // ── Single piece mode ─────────────────────────────────────────────────
    bool  SinglePieceMode = false, // false follows the web general flow: split processed PLY, export largest object
    int   MeshBridgeFill  = 30);   // max grid gap to bridge between scan regions / flanges

public record PipelineResult(
    List<ClusterObject> Objects,
    byte[] ZipBytes,
    byte[] CoordsJsonBytes   // merged JSON of all objects (raw-scanner coords)
);

public static class Pipeline
{
    /// <summary>
    /// Full one-click pipeline — takes the RAW scanner PLY and does everything:
    ///   1. ScanProcessor: PCA ground-plane detection → z_thresh filter → denoise
    ///      (equivalent to what the bundled Python backend does server-side)
    ///   2. One-click angle-steel mode: keep the filtered scan as one target piece
    ///   3. Export PLY + JSON (with raw scanner coords) + STEP + glTF + 3× PNG
    ///   4. Package everything into a ZIP
    /// </summary>
    public static PipelineResult Run(
        Stream rawScanStream,
        string baseName,
        PipelineOptions? opts = null)
    {
        opts ??= new();

        // 1. Parse the raw scan
        var (rawPts, _) = PlyReader.Parse(rawScanStream);

        // 2. Ground-plane removal + z_thresh filter + denoise  (replaces Python backend)
        var outPts = ScanProcessor.Process(rawPts, opts.ZThresh, opts.MinNoiseSize, opts.NoiseVoxel);
        return BuildExports(rawPts, outPts, baseName, opts);
    }

    /// <summary>對已解析的原始點與處理後點做分群／匯出（ZIP／JSON／PLY／網格）。</summary>
    public static PipelineResult Export(
        PlyPoint[] rawPts,
        PlyPoint[] processedPts,
        string baseName,
        PipelineOptions? opts = null)
    {
        opts ??= new();
        return BuildExports(rawPts, processedPts, baseName, opts);
    }

    /// <summary>
    /// Export from Python/backend-processed PLY. This mirrors the web general flow after
    /// /process has produced its output PLY.
    /// </summary>
    public static PipelineResult RunProcessed(
        Stream processedPlyStream,
        string baseName,
        PipelineOptions? opts = null)
    {
        opts ??= new();
        var (outPts, _) = PlyReader.Parse(processedPlyStream);
        return BuildExports(outPts, outPts, baseName, opts);
    }

    static PipelineResult BuildExports(PlyPoint[] rawPts, PlyPoint[] outPts, string baseName, PipelineOptions opts)
    {
        // Match the web general flow's effective target: the processed angle-steel PLY
        //    is exported as one piece. C# clustering can split the two flanges, so keep all
        //    significant clusters and merge them back into one target object before meshing.
        PlyPoint[] ptsForCluster = opts.RemoveDominantPlane
            ? PcaFitter.RemoveDominantPlane(outPts, opts.PlaneTol)
            : outPts;
        var splitObjects = VoxelClusterer.Cluster(
            ptsForCluster,
            baseName,
            Math.Max(opts.VoxelSize, 30f),
            opts.MinClusterSize);
        splitObjects = MergeAdjacentObjects(splitObjects, opts.PlaneTol);
        if (splitObjects.Count == 0)
            throw new InvalidOperationException("分群後沒有找到任何物件（點數太少或參數需調整）");
        var targetPts = splitObjects.SelectMany(o => o.Points).ToArray();
        var objects = new List<ClusterObject> { new ClusterObject(baseName, targetPts) };

        // 4. Build raw-coordinate lookup: each processed point → its original raw index
        //    Since outPts is a filtered/reordered subset of rawPts we match by coordinate value
        //    (both came from the same parse, so float bits are identical).
        var coordToRawIdx = new Dictionary<(int,int,int), int>(rawPts.Length);
        for (int i = 0; i < rawPts.Length; i++)
        {
            var key = (BitConverter.SingleToInt32Bits(rawPts[i].X),
                       BitConverter.SingleToInt32Bits(rawPts[i].Y),
                       BitConverter.SingleToInt32Bits(rawPts[i].Z));
            coordToRawIdx.TryAdd(key, i);
        }

        Dictionary<(int,int,int), int>? coordToOutIdx = null;
        coordToOutIdx = new(outPts.Length);
        for (int i = 0; i < outPts.Length; i++)
        {
            var key = (BitConverter.SingleToInt32Bits(outPts[i].X),
                       BitConverter.SingleToInt32Bits(outPts[i].Y),
                       BitConverter.SingleToInt32Bits(outPts[i].Z));
            coordToOutIdx.TryAdd(key, i);
        }

        // 5. Build per-object exports
        var zipEntries = new List<(string Name, byte[] Data)>();
        var allCoordsJson = new System.Text.StringBuilder();
        allCoordsJson.AppendLine("[");

        for (int objIdx = 0; objIdx < objects.Count; objIdx++)
        {
            var obj = objects[objIdx];

            // Map each object point → raw scanner index via float-bit key lookup
            var objMapping = new int[obj.Points.Length];
            for (int j = 0; j < obj.Points.Length; j++)
            {
                var key = (BitConverter.SingleToInt32Bits(obj.Points[j].X),
                           BitConverter.SingleToInt32Bits(obj.Points[j].Y),
                           BitConverter.SingleToInt32Bits(obj.Points[j].Z));
                objMapping[j] = coordToRawIdx.TryGetValue(key, out int ri) ? ri : -1;
            }

            // PLY (with embedded raw coords columns)
            zipEntries.Add(($"{obj.Name}.ply", PlyExporter.Export(obj.Points, rawPts, objMapping)));

            // Coords JSON (both inside ZIP and for standalone download)
            var jsonBytes = JsonExporter.Export(obj, rawPts, objMapping);
            zipEntries.Add(($"{obj.Name}.json", jsonBytes));

            // Append to merged coords JSON
            var objJson = System.Text.Encoding.UTF8.GetString(jsonBytes).Trim();
            allCoordsJson.Append(objIdx == 0 ? "  " : ", ");
            allCoordsJson.AppendLine(objJson.Replace("\n", "\n  "));
            if (objIdx < objects.Count - 1) allCoordsJson.AppendLine(",");

            // 2D views
            zipEntries.Add(($"{obj.Name}_2d_top.png",   OrthoViewRenderer.Render(obj.Points, OrthoViewRenderer.View.Top)));
            zipEntries.Add(($"{obj.Name}_2d_front.png", OrthoViewRenderer.Render(obj.Points, OrthoViewRenderer.View.Front)));
            zipEntries.Add(($"{obj.Name}_2d_side.png",  OrthoViewRenderer.Render(obj.Points, OrthoViewRenderer.View.Side)));

            // Mesh → STEP + GLB
            try {
                MeshBuilder.Mesh mesh;
                mesh = HeightfieldMeshBuilder.Build(
                    obj.Points,
                    opts.MeshCellSize,
                    opts.MeshThickness,
                    opts.SmoothIter,
                    opts.ErodeN,
                    opts.MeshBridgeFill);
                zipEntries.Add(($"{obj.Name}.stp", StepExporter.Export(mesh, obj.Name)));
                zipEntries.Add(($"{obj.Name}.glb", GltfExporter.Export(mesh)));
            } catch { /* mesh build can fail on very sparse/small objects; skip */ }
        }

        allCoordsJson.AppendLine("]");
        byte[] coordsJson = System.Text.Encoding.UTF8.GetBytes(allCoordsJson.ToString());

        // 6. Package ZIP
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in zipEntries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                using var es = entry.Open();
                es.Write(data);
            }

        return new PipelineResult(objects, ms.ToArray(), coordsJson);
    }

    static List<ClusterObject> MergeAdjacentObjects(List<ClusterObject> objects, float planeTol)
    {
        if (objects.Count < 2) return objects;

        float threshold = Math.Max(planeTol * 3f, 30f);
        var boxes = objects.Select(o => BBox(o.Points)).ToArray();
        var parent = Enumerable.Range(0, objects.Count).ToArray();

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        for (int i = 0; i < objects.Count; i++)
        for (int j = i + 1; j < objects.Count; j++)
            if (BBoxGap(boxes[i], boxes[j]) <= threshold)
                Union(i, j);

        var groups = new Dictionary<int, List<ClusterObject>>();
        for (int i = 0; i < objects.Count; i++)
        {
            int root = Find(i);
            if (!groups.TryGetValue(root, out var list))
                groups[root] = list = [];
            list.Add(objects[i]);
        }

        var merged = new List<ClusterObject>();
        foreach (var group in groups.Values)
        {
            if (group.Count == 1)
            {
                merged.Add(group[0]);
                continue;
            }

            var name = group.OrderBy(g => g.Name).First().Name;
            var points = group.SelectMany(g => g.Points).ToArray();
            merged.Add(new ClusterObject(name, points));
        }

        merged.Sort((a, b) => b.Points.Length.CompareTo(a.Points.Length));
        return merged;
    }

    static (float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ) BBox(PlyPoint[] pts)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        foreach (var p in pts)
        {
            if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
        }
        return (minX, maxX, minY, maxY, minZ, maxZ);
    }

    static float BBoxGap((float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ) a,
        (float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ) b)
    {
        float gx = Math.Max(0, Math.Max(a.MinX, b.MinX) - Math.Min(a.MaxX, b.MaxX));
        float gy = Math.Max(0, Math.Max(a.MinY, b.MinY) - Math.Min(a.MaxY, b.MaxY));
        float gz = Math.Max(0, Math.Max(a.MinZ, b.MinZ) - Math.Min(a.MaxZ, b.MaxZ));
        return MathF.Sqrt(gx * gx + gy * gy + gz * gz);
    }
}
