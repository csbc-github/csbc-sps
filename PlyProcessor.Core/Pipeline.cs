using System.IO.Compression;

namespace PlyProcessor.Core;

public record PipelineOptions(
    // ── Scan processing (replaces Python backend) ─────────────────────────
    // float ZThresh       = 12f,    // 舊固定高度門檻；ScanProcessor 改自動空隙，已不傳入
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
    bool  HasInnerLeft = false,
    float InnerLeftX = 0f,
    float InnerLeftY = 0f,
    float InnerLeftZ = 0f,
    bool  HasInnerRight = false,
    float InnerRightX = 0f,
    float InnerRightY = 0f,
    float InnerRightZ = 0f,
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
/// 可量測或帶入兩翼厚度供紀錄；STEP／GLB 為單一高度場，有兩側厚度時取平均擠出，無量測時用 <c>MeshThickness</c>（預設 6 mm）。
/// </summary>
public static class Pipeline
{
    // 目前沒有呼叫：舊入口 Pipeline.Run（raw 串流一次濾點+匯出）。
    // 現場／API 改走 RawPlyScanPipeline → ScanProcessor.Process → Pipeline.Export。
#if false
    /// <summary>
    /// 從原始掃描 PLY 串流跑完整管線：濾地／去雜訊、分群合併、匯出 ZIP（PLY／JSON／STEP／GLB／PNG）。
    /// </summary>
    public static PipelineResult Run(
        Stream rawScanStream,
        string baseName,
        PipelineOptions? opts = null)
    {
        opts ??= new();

        // 1. Parse the raw scan
        var (rawPts, _) = PlyReader.Parse(rawScanStream);

        // 2. Ground-plane removal + denoise  (replaces Python backend)
        var outPts = ScanProcessor.Process(rawPts, opts.MinNoiseSize, opts.NoiseVoxel);
        return BuildExports(rawPts, outPts, baseName, opts);
    }
#endif

    /// <summary>
    /// 對已解析的原始點與濾點後點做分群與匯出。
    /// </summary>
    /// <param name="rawPts">raw PLY 全部點（對應 JSON 的 raw 欄）。</param>
    /// <param name="processedPts">去地／去雜訊後的點。</param>
    /// <param name="baseName">匯出檔名前綴。</param>
    /// <param name="opts">
    /// 若 <c>MeasureFlangeThickness</c>：依規格 v1.1 §9.8 量兩斜面；STEP／GLB 用單一高度場，厚度取兩側平均（僅一側則用該側）。
    /// 若 <c>UseStoredFlangeThickness</c>：用已存左右厚度的平均擠出。
    /// 皆無則擠出 <c>MeshThickness</c>（預設 6 mm）。
    /// </param>
    /// <returns>ZIP、座標 JSON，以及量測時的左側／右側厚度。</returns>
    public static PipelineResult Export(
        PlyPoint[] rawPts,
        PlyPoint[] processedPts,
        string baseName,
        PipelineOptions? opts = null)
    {
        opts ??= new();
        return BuildExports(rawPts, processedPts, baseName, opts);
    }

    // 目前沒有呼叫：舊入口 Pipeline.RunProcessed（已濾點 PLY 直接匯出）。
#if false
    /// <summary>
    /// 從「已經濾點」的 PLY 匯出（對齊舊網頁 /process 之後的流程）。
    /// raw 與 processed 使用同一批點。
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
#endif

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
        // 30 mm 體素分群後只留最大群；再用 10 mm 丟掉黏在其上的小島（兩翼夠大會留下）。
        PlyPoint[] ptsForCluster = opts.RemoveDominantPlane
            ? PcaFitter.RemoveDominantPlane(outPts, opts.PlaneTol)
            : outPts;
        var splitObjects = VoxelClusterer.Cluster(
            ptsForCluster,
            baseName,
            Math.Max(opts.VoxelSize, 30f),
            opts.MinClusterSize);
        // 舊做法：包圍盒間距 ≤ max(PlaneTol*3, 30) 的群合併（兩翼被切開時可併回）。
        // splitObjects = MergeAdjacentObjects(splitObjects, opts.PlaneTol);
        if (splitObjects.Count == 0)
            throw new InvalidOperationException("分群後沒有找到任何物件（點數太少或參數需調整）");
        // 舊做法：把所有分群（含遠處雜點）全部併成一個 angle_steel。
        // var targetPts = splitObjects.SelectMany(o => o.Points).ToArray();
        var targetPts = DropSmallIslands(
            splitObjects[0].Points,
            baseName,
            10f,
            opts.MinClusterSize);
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
                float? leftX = null;
                float? leftY = null;
                float? leftZ = null;
                if (opts.HasInnerLeft)
                {
                    leftX = opts.InnerLeftX;
                    leftY = opts.InnerLeftY;
                    leftZ = opts.InnerLeftZ;
                }

                float? rightX = null;
                float? rightY = null;
                float? rightZ = null;
                if (opts.HasInnerRight)
                {
                    rightX = opts.InnerRightX;
                    rightY = opts.InnerRightY;
                    rightZ = opts.InnerRightZ;
                }

                var measured = FlangeThicknessMeasurer.Measure(
                    obj.Points,
                    leftX,
                    leftY,
                    leftZ,
                    rightX,
                    rightY,
                    rightZ);
                thicknessA = measured.ThicknessLeft;
                thicknessB = measured.ThicknessRight;
            }

            // Mesh → STEP + GLB：單一高度場（不分翼），厚度用量測／已存值；兩側都有則取平均。
            MeshBuilder.Mesh mesh;
            float meshThick = opts.MeshThickness;
            if (opts.MeasureFlangeThickness && thicknessA is not null && thicknessB is not null)
            {
                meshThick = 0.5f * ((float)thicknessA.Value + (float)thicknessB.Value);
                float probeX = opts.InnerLeftX;
                float probeY = opts.InnerLeftY;
                if (!opts.HasInnerLeft)
                {
                    probeX = opts.InnerRightX;
                    probeY = opts.InnerRightY;
                }

                if (AngleSteelMeshBuilder.TrySplitFlanges(obj.Points, out _, out _, out var isLow))
                {
                    int iA = FlangeThicknessMeasurer.FindNearestXyIndex(obj.Points, probeX, probeY);
                    thicknessAOnLowFlange = isLow[iA];
                }
            }
            else if (opts.MeasureFlangeThickness && thicknessA is not null)
            {
                meshThick = (float)thicknessA.Value;
            }
            else if (opts.MeasureFlangeThickness && thicknessB is not null)
            {
                meshThick = (float)thicknessB.Value;
            }
            else if (opts.UseStoredFlangeThickness)
            {
                meshThick = 0.5f * (opts.StoredThicknessA + opts.StoredThicknessB);
                thicknessAOnLowFlange = opts.StoredThicknessAOnLowFlange;
            }

            try
            {
                mesh = HeightfieldMeshBuilder.Build(
                    obj.Points,
                    opts.MeshCellSize,
                    meshThick,
                    opts.SmoothIter,
                    opts.ErodeN,
                    opts.MeshBridgeFill);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"物件 {obj.Name} 的網格建立失敗，已中止 STEP／GLB 匯出。",
                    ex);
            }

            byte[] glbBytes;
            byte[] stepBytes;
            Exception? glbError = null;
            Exception? stepError = null;
            try
            {
                stepBytes = StepExporter.Export(mesh, obj.Name);
            }
            catch (Exception ex)
            {
                stepBytes = Array.Empty<byte>();
                stepError = ex;
            }

            try
            {
                glbBytes = GltfExporter.Export(mesh);
            }
            catch (Exception ex)
            {
                glbBytes = Array.Empty<byte>();
                glbError = ex;
            }

            if (stepError is not null || glbError is not null)
            {
                throw new InvalidOperationException(
                    DescribeMeshExportFailure(glbError, obj.Name, stepError),
                    stepError ?? glbError);
            }

            zipEntries.Add(($"{obj.Name}.stp", stepBytes));
            zipEntries.Add(($"{obj.Name}.glb", glbBytes));
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
    /// 組 STEP／GLB 分開失敗時的訊息。成功的那一邊也不會靜默略過。
    /// </summary>
    static string DescribeMeshExportFailure(Exception? glbError, string objectName, Exception? stepError)
    {
        string glbText = glbError is null ? "GLB 已產生，但因另一項失敗而未寫入。" : "GLB 匯出失敗：" + glbError.Message;
        string stepText = stepError is null ? "STEP 已產生，但因另一項失敗而未寫入。" : "STEP 匯出失敗：" + stepError.Message;
        return $"物件 {objectName} 的網格匯出失敗。{stepText} {glbText}";
    }

    /// <summary>
    /// 對已取出的最大群再以較小體素分群，丟掉遠小於主體的島。
    /// 兩翼若被細分切開但都夠大則一併保留。
    /// </summary>
    static PlyPoint[] DropSmallIslands(
        PlyPoint[] pts,
        string baseName,
        float fineVoxelMm,
        int minClusterSize)
    {
        var fine = VoxelClusterer.Cluster(pts, baseName, fineVoxelMm, minClusterSize);
        if (fine.Count == 0)
        {
            return pts;
        }

        const float keepRatio = 0.05f;
        int largest = fine[0].Points.Length;
        int minKeep = (int)(largest * keepRatio);
        if (minKeep < minClusterSize)
        {
            minKeep = minClusterSize;
        }

        int total = 0;
        for (int i = 0; i < fine.Count; i++)
        {
            if (fine[i].Points.Length >= minKeep)
            {
                total += fine[i].Points.Length;
            }
        }

        if (total == 0)
        {
            return fine[0].Points;
        }

        var kept = new PlyPoint[total];
        int w = 0;
        for (int i = 0; i < fine.Count; i++)
        {
            if (fine[i].Points.Length < minKeep)
            {
                continue;
            }

            PlyPoint[] src = fine[i].Points;
            for (int j = 0; j < src.Length; j++)
            {
                kept[w] = src[j];
                w++;
            }
        }

        return kept;
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
