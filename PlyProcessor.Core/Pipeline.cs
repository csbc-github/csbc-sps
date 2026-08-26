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
    int   MeshBridgeFill  = 30,    // max grid gap to bridge between scan regions / flanges
    bool  MeasureFlangeThickness = false,
    float ThicknessAx = 0f,
    float ThicknessAy = 0f,
    float ThicknessAz = 0f,
    float ThicknessBx = 0f,
    float ThicknessBy = 0f,
    float ThicknessBz = 0f,
    bool  UseStoredFlangeThickness = false,
    float StoredThicknessA = 0f,
    float StoredThicknessB = 0f,
    bool  StoredThicknessAOnLowFlange = true);

public record PipelineResult(
    List<ClusterObject> Objects,
    byte[] ZipBytes,
    byte[] CoordsJsonBytes,  // merged JSON of all objects (raw-scanner coords)
    double? ThicknessA = null,
    double? ThicknessB = null,
    bool? ThicknessAOnLowFlange = null
);

/// <summary>
/// 角鋼點雲處理與匯出：濾地、分群、PLY／JSON／STEP／GLB／PNG 打包 ZIP。
/// 可用夾具點量測兩翼厚度、或帶入已存厚度組網格；皆無則用 <c>MeshThickness</c>。
/// </summary>
public static class Pipeline
{
    /// <summary>
    /// 從原始掃描 PLY 串流跑完整管線：濾地／去雜訊、分群合併、匯出 ZIP（PLY／JSON／STEP／GLB／PNG）。
    /// </summary>
    /// <param name="rawScanStream">掃描器座標的 raw PLY。</param>
    /// <param name="baseName">匯出檔名前綴，面掃描固定用 angle_steel。</param>
    /// <param name="opts">濾點與網格參數；null 則用預設（含 MeshThickness 6 mm）。</param>
    /// <returns>分群物件、ZIP 位元組、合併座標 JSON，以及可選的量測厚度。</returns>
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

    /// <summary>
    /// 對已解析的原始點與濾點後點做分群與匯出。
    /// </summary>
    /// <param name="rawPts">raw PLY 全部點（對應 JSON 的 raw 欄）。</param>
    /// <param name="processedPts">去地／去雜訊後的點。</param>
    /// <param name="baseName">匯出檔名前綴。</param>
    /// <param name="opts">
    /// 若 <c>MeasureFlangeThickness</c>：用夾具 XYZ 量兩翼並組雙厚度網格。
    /// 若 <c>UseStoredFlangeThickness</c>：用已存厚度組網格。
    /// 兩者皆否：用 <c>MeshThickness</c>（預設 6 mm）。
    /// </param>
    /// <returns>ZIP、座標 JSON，以及量測時的 thicknessA／B 與 A 是否在低翼。</returns>
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
    /// 從「已經濾點」的 PLY 匯出（對齊舊網頁 /process 之後的流程）。
    /// raw 與 processed 使用同一批點。
    /// </summary>
    /// <param name="processedPlyStream">濾點後 PLY。</param>
    /// <param name="baseName">匯出檔名前綴。</param>
    /// <param name="opts">網格與分群參數。</param>
    /// <returns>與 <see cref="Run"/> 相同結構的匯出結果。</returns>
    public static PipelineResult RunProcessed(
        Stream processedPlyStream,
        string baseName,
        PipelineOptions? opts = null)
    {
        opts ??= new();
        var (outPts, _) = PlyReader.Parse(processedPlyStream);
        return BuildExports(outPts, outPts, baseName, opts);
    }

    /// <summary>
    /// 分群、合併相鄰物件後，寫 PLY／JSON／正交圖，並依選項組網格。
    /// 網格失敗時略過 STEP／GLB，其餘檔仍進 ZIP。
    /// </summary>
    /// <param name="rawPts">原始掃描點，供 JSON raw 欄對照。</param>
    /// <param name="outPts">濾點後的點。</param>
    /// <param name="baseName">單一目標物件名稱。</param>
    /// <param name="opts">濾點已完成；此處用分群、網格與厚度旗標。</param>
    /// <returns>合併為一個物件的結果、ZIP 與座標 JSON。</returns>
    /// <exception cref="InvalidOperationException">分群後沒有任何物件。</exception>
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
        double? thicknessA = null;
        double? thicknessB = null;
        bool? thicknessAOnLowFlange = null;

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

            if (opts.MeasureFlangeThickness && thicknessA is null)
            {
                var measured = FlangeThicknessMeasurer.Measure(
                    obj.Points,
                    opts.ThicknessAx,
                    opts.ThicknessAy,
                    opts.ThicknessAz,
                    opts.ThicknessBx,
                    opts.ThicknessBy,
                    opts.ThicknessBz);
                thicknessA = measured.ThicknessA;
                thicknessB = measured.ThicknessB;
            }

            // Mesh → STEP + GLB
            try {
                MeshBuilder.Mesh mesh;
                if (opts.MeasureFlangeThickness && thicknessA is not null && thicknessB is not null)
                {
                    mesh = AngleSteelMeshBuilder.BuildDualThickness(
                        obj.Points,
                        opts.MeshCellSize,
                        (float)thicknessA.Value,
                        (float)thicknessB.Value,
                        opts.ThicknessAx,
                        opts.ThicknessAy,
                        opts.SmoothIter,
                        opts.ErodeN,
                        opts.MeshBridgeFill,
                        out var aOnLow);
                    thicknessAOnLowFlange = aOnLow;
                }
                else if (opts.UseStoredFlangeThickness)
                {
                    mesh = AngleSteelMeshBuilder.BuildDualThicknessMapped(
                        obj.Points,
                        opts.MeshCellSize,
                        opts.StoredThicknessA,
                        opts.StoredThicknessB,
                        opts.StoredThicknessAOnLowFlange,
                        opts.SmoothIter,
                        opts.ErodeN,
                        opts.MeshBridgeFill);
                }
                else
                {
                    mesh = HeightfieldMeshBuilder.Build(
                        obj.Points,
                        opts.MeshCellSize,
                        opts.MeshThickness,
                        opts.SmoothIter,
                        opts.ErodeN,
                        opts.MeshBridgeFill);
                }
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

        return new PipelineResult(objects, ms.ToArray(), coordsJson, thicknessA, thicknessB, thicknessAOnLowFlange);
    }

    /// <summary>
    /// 將包圍盒間距小於門檻的分群物件合併。
    /// </summary>
    /// <param name="objects">體素分群結果。</param>
    /// <param name="planeTol">平面容差（mm）；合併門檻為 max(3×此值, 30)。</param>
    /// <returns>合併後由大到小排序的物件清單。</returns>
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

    /// <summary>
    /// 計算點雲軸對齊包圍盒，供相鄰分群合併判斷。
    /// </summary>
    /// <param name="pts">該分群的點。</param>
    /// <returns>XYZ 最小／最大值。</returns>
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

    /// <summary>
    /// 兩個軸對齊包圍盒在 3D 的間隙長度（已重疊的軸貢獻 0）。
    /// </summary>
    /// <param name="a">第一個包圍盒。</param>
    /// <param name="b">第二個包圍盒。</param>
    /// <returns>間隙歐氏長度（mm）；完全重疊或接觸時為 0。</returns>
    static float BBoxGap((float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ) a,
        (float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ) b)
    {
        float gx = Math.Max(0, Math.Max(a.MinX, b.MinX) - Math.Min(a.MaxX, b.MaxX));
        float gy = Math.Max(0, Math.Max(a.MinY, b.MinY) - Math.Min(a.MaxY, b.MaxY));
        float gz = Math.Max(0, Math.Max(a.MinZ, b.MinZ) - Math.Min(a.MaxZ, b.MaxZ));
        return MathF.Sqrt(gx * gx + gy * gy + gz * gz);
    }
}
