namespace PlyProcessor.Core;

/// <summary>
/// Builds a mesh approximation for angle steel by splitting the point cloud into
/// its two flanges and building a correctly-oriented flat plate for each.
///
/// Compared to the naive single-plane MeshBuilder, this ensures each flange's
/// mesh is extruded perpendicular to THAT FLANGE's actual surface — not some
/// averaged plane — so the two plates come out at the correct ~90° dihedral angle
/// instead of being parallel to each other.
///
/// The two plates share the bend-ridge edge only approximately (they are separate
/// meshes that meet at the corner). Full seamless L-shape reconstruction is a
/// future enhancement.
/// </summary>
public static class AngleSteelMeshBuilder
{
    /// <summary>
    /// 以單一擠出厚度組 L 型近似網格：先嘗試分成兩翼，各用自己的平面擠出後合併。
    /// 分不出兩翼時退回 <see cref="MeshBuilder.Build"/>（整片同一厚度）。
    /// </summary>
    /// <param name="pts">角鋼點雲（mm）。</param>
    /// <param name="cellSize">投影網格邊長（mm）。</param>
    /// <param name="thickness">兩翼共用的擠出厚度（mm）。</param>
    /// <param name="smoothIter">網格平滑次數。</param>
    /// <param name="erodeN">佔用格侵蝕次數。</param>
    /// <returns>合併後的三角網格。</returns>
    public static MeshBuilder.Mesh Build(
        PlyPoint[] pts,
        float cellSize   = 2f,
        float thickness  = 6f,
        int   smoothIter = 4,
        int   erodeN     = 1)
    {
        if (!TrySplitFlanges(pts, out var flangeA, out var flangeB, out _))
        {
            return MeshBuilder.Build(pts, cellSize, thickness, smoothIter, erodeN);
        }

        var meshA = MeshBuilder.Build(flangeA, cellSize, thickness, smoothIter, erodeN);
        var meshB = MeshBuilder.Build(flangeB, cellSize, thickness, smoothIter, erodeN);
        return Combine(meshA, meshB);
    }

    /// <summary>
    /// 兩翼各用 <paramref name="thicknessA"/>／<paramref name="thicknessB"/> 組網格。
    /// 以夾具 A 的 XY 在點雲找最近點，該點所在翼用 A 的厚度，另一翼用 B。
    /// 分不出兩翼時改用單一 heightfield，厚度為兩者平均。
    /// </summary>
    /// <param name="pts">角鋼點雲（mm），應與夾具座標同一補償框。</param>
    /// <param name="cellSize">heightfield 格子邊長（mm）。</param>
    /// <param name="thicknessA">翼 A 擠出厚度（mm）。</param>
    /// <param name="thicknessB">翼 B 擠出厚度（mm）。</param>
    /// <param name="probeAx">夾具 A 的 X，用來決定哪一翼吃 thicknessA。</param>
    /// <param name="probeAy">夾具 A 的 Y。</param>
    /// <param name="smoothIter">高度平滑次數。</param>
    /// <param name="erodeN">佔用格侵蝕次數。</param>
    /// <param name="bridgeFill">兩翼投影缺口要橋接的最大格數。</param>
    /// <param name="thicknessAOnLowFlange">傳出：A 是否落在直方圖較低的那翼。</param>
    /// <returns>兩翼合併網格，或分翼失敗時的單一 heightfield。</returns>
    public static MeshBuilder.Mesh BuildDualThickness(
        PlyPoint[] pts,
        float cellSize,
        float thicknessA,
        float thicknessB,
        float probeAx,
        float probeAy,
        int smoothIter,
        int erodeN,
        int bridgeFill,
        out bool thicknessAOnLowFlange)
    {
        thicknessAOnLowFlange = true;
        if (!TrySplitFlanges(pts, out _, out _, out var isLow))
        {
            float average = (thicknessA + thicknessB) * 0.5f;
            return HeightfieldMeshBuilder.Build(
                pts, cellSize, average, smoothIter, erodeN, bridgeFill);
        }

        int iA = FlangeThicknessMeasurer.FindNearestXyIndex(pts, probeAx, probeAy);
        thicknessAOnLowFlange = isLow[iA];
        return BuildDualThicknessMapped(
            pts,
            cellSize,
            thicknessA,
            thicknessB,
            thicknessAOnLowFlange,
            smoothIter,
            erodeN,
            bridgeFill);
    }

    /// <summary>
    /// 以 <paramref name="thicknessAOnLowFlange"/> 把 A／B 對到直方圖低／高翼組網格。
    /// 分不出兩翼時改用平均厚度的單一 heightfield。
    /// </summary>
    /// <param name="pts">角鋼點雲（mm）。</param>
    /// <param name="cellSize">heightfield 格子邊長（mm）。</param>
    /// <param name="thicknessA">翼 A 厚度（mm）。</param>
    /// <param name="thicknessB">翼 B 厚度（mm）。</param>
    /// <param name="thicknessAOnLowFlange">A 是否在直方圖較低的那翼。</param>
    /// <param name="smoothIter">高度平滑次數。</param>
    /// <param name="erodeN">佔用格侵蝕次數。</param>
    /// <param name="bridgeFill">兩翼投影缺口要橋接的最大格數。</param>
    /// <returns>兩翼合併網格，或分翼失敗時的單一 heightfield。</returns>
    public static MeshBuilder.Mesh BuildDualThicknessMapped(
        PlyPoint[] pts,
        float cellSize,
        float thicknessA,
        float thicknessB,
        bool thicknessAOnLowFlange,
        int smoothIter,
        int erodeN,
        int bridgeFill)
    {
        if (!TrySplitFlanges(pts, out var flangeLow, out var flangeHigh, out _))
        {
            float average = (thicknessA + thicknessB) * 0.5f;
            return HeightfieldMeshBuilder.Build(
                pts, cellSize, average, smoothIter, erodeN, bridgeFill);
        }

        float thickLow = thicknessB;
        float thickHigh = thicknessA;
        if (thicknessAOnLowFlange)
        {
            thickLow = thicknessA;
            thickHigh = thicknessB;
        }

        var meshLow = HeightfieldMeshBuilder.Build(
            flangeLow, cellSize, thickLow, smoothIter, erodeN, bridgeFill);
        var meshHigh = HeightfieldMeshBuilder.Build(
            flangeHigh, cellSize, thickHigh, smoothIter, erodeN, bridgeFill);
        return Combine(meshLow, meshHigh);
    }

    /// <summary>
    /// 以主平面高度直方圖的谷底把點雲分成兩翼（低翼／高翼）。
    /// 點太少、或某一翼佔比低於 5%／高於 95% 時視為無法分割。
    /// </summary>
    /// <param name="pts">待分割點雲。</param>
    /// <param name="flangeLow">成功時：直方圖較低一側的點。</param>
    /// <param name="flangeHigh">成功時：直方圖較高一側的點。</param>
    /// <param name="isLow">成功時：與 <paramref name="pts"/> 等長，true 表示該點屬低翼。</param>
    /// <returns>成功分成兩翼為 true；失敗時三個 out 皆為空陣列。</returns>
    public static bool TrySplitFlanges(
        PlyPoint[] pts,
        out PlyPoint[] flangeLow,
        out PlyPoint[] flangeHigh,
        out bool[] isLow)
    {
        flangeLow = Array.Empty<PlyPoint>();
        flangeHigh = Array.Empty<PlyPoint>();
        isLow = Array.Empty<bool>();

        if (pts.Length < 100)
        {
            return false;
        }

        var plane = PcaFitter.Fit(pts);
        var heights = new float[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            float dx = pts[i].X - plane.Cx;
            float dy = pts[i].Y - plane.Cy;
            float dz = pts[i].Z - plane.Cz;
            heights[i] = dx * plane.E3[0] + dy * plane.E3[1] + dz * plane.E3[2];
        }

        if (heights.Average() < 0)
        {
            for (int i = 0; i < heights.Length; i++)
            {
                heights[i] = -heights[i];
            }
        }

        float hMin = heights.Min();
        float hMax = heights.Max();
        float hRange = hMax - hMin;
        float splitH = FindHistogramValley(heights, hMin, hRange);

        isLow = new bool[pts.Length];
        int nLow = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            if (heights[i] <= splitH)
            {
                isLow[i] = true;
                nLow++;
            }
        }

        int nHigh = pts.Length - nLow;
        float ratioLow = (float)nLow / pts.Length;
        if (nLow < 50 || nHigh < 50 || ratioLow < 0.05f || ratioLow > 0.95f)
        {
            isLow = Array.Empty<bool>();
            return false;
        }

        flangeLow = new PlyPoint[nLow];
        flangeHigh = new PlyPoint[nHigh];
        int iLow = 0;
        int iHigh = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            if (isLow[i])
            {
                flangeLow[iLow] = pts[i];
                iLow++;
            }
            else
            {
                flangeHigh[iHigh] = pts[i];
                iHigh++;
            }
        }

        return true;
    }

    /// <summary>
    /// 在高度直方圖上找兩峰之間的谷底，作為兩翼分界高度。
    /// 前半／後半各取一個峰，再在兩峰之間取平滑後計數最低的 bin。
    /// </summary>
    /// <param name="heights">各點相對主平面的高度（已翻成多數為正）。</param>
    /// <param name="hMin">高度最小值。</param>
    /// <param name="hRange">高度全距；近乎零時回傳區間中點。</param>
    /// <returns>分割高度，落在谷底 bin 的中心。</returns>
    static float FindHistogramValley(float[] heights, float hMin, float hRange)
    {
        const int nBins = 50;
        if (hRange < 1e-4f) return hMin + hRange / 2f;

        float binW = hRange / nBins;
        var bins = new int[nBins];
        foreach (var h in heights)
            bins[Math.Min(nBins - 1, (int)((h - hMin) / binW))]++;

        // Smooth the histogram slightly
        var smooth = new float[nBins];
        for (int b = 0; b < nBins; b++)
        {
            int lo = Math.Max(0, b - 2), hi = Math.Min(nBins - 1, b + 2);
            float s = 0; int cnt = 0;
            for (int k = lo; k <= hi; k++) { s += bins[k]; cnt++; }
            smooth[b] = s / cnt;
        }

        // Find peak in lower half (dominant flange A)
        int peak1 = 0;
        for (int b = 1; b < nBins / 2; b++)
            if (smooth[b] > smooth[peak1]) peak1 = b;

        // Find peak in upper half (flange B, if present)
        int peak2 = nBins / 2;
        for (int b = nBins / 2 + 1; b < nBins; b++)
            if (smooth[b] > smooth[peak2]) peak2 = b;

        // Valley between the two peaks
        int valley = peak1;
        for (int b = peak1; b <= peak2; b++)
            if (smooth[b] < smooth[valley]) valley = b;

        return hMin + (valley + 0.5f) * binW;
    }

    /// <summary>
    /// 把兩片網格頂點與三角形合併成一個 <see cref="MeshBuilder.Mesh"/>；
    /// 第二片的三角形索引會加上第一片的頂點數。
    /// </summary>
    /// <param name="a">第一片（低翼或翼 A）。</param>
    /// <param name="b">第二片（高翼或翼 B）。</param>
    /// <returns>共用一套頂點／三角形清單的合併網格。</returns>
    static MeshBuilder.Mesh Combine(MeshBuilder.Mesh a, MeshBuilder.Mesh b)
    {
        int offset = a.Vertices.Count;
        var verts = new List<float[]>(a.Vertices.Concat(b.Vertices));
        var tris  = new List<int[]>(a.Triangles.Concat(
            b.Triangles.Select(t => new[]{ t[0]+offset, t[1]+offset, t[2]+offset })));
        return new MeshBuilder.Mesh(verts, tris);
    }
}
