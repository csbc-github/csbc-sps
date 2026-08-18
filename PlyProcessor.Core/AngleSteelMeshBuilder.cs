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
    public static MeshBuilder.Mesh Build(
        PlyPoint[] pts,
        float cellSize   = 2f,
        float thickness  = 6f,
        int   smoothIter = 4,
        int   erodeN     = 1)
    {
        if (pts.Length < 100)
            return MeshBuilder.Build(pts, cellSize, thickness, smoothIter, erodeN);

        // ── 1. PCA to get eMin (surface normal of dominant face) ──────────────
        var plane = PcaFitter.Fit(pts);

        // Heights above the dominant PCA plane
        float[] heights = new float[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            float dx = pts[i].X - plane.Cx;
            float dy = pts[i].Y - plane.Cy;
            float dz = pts[i].Z - plane.Cz;
            heights[i] = dx*plane.E3[0] + dy*plane.E3[1] + dz*plane.E3[2];
        }

        // Ensure heights are measured from the correct side (positive toward objects)
        if (heights.Average() < 0)
            for (int i = 0; i < heights.Length; i++) heights[i] = -heights[i];

        // ── 2. Find the split point using a height histogram valley ────────────
        float hMin = heights.Min(), hMax = heights.Max();
        float hRange = hMax - hMin;

        float splitH = FindHistogramValley(heights, hMin, hRange);

        var flangeA = pts.Where((_, i) => heights[i] <= splitH).ToArray();
        var flangeB = pts.Where((_, i) => heights[i] >  splitH).ToArray();

        // Both flanges must have at least 5% of total points to be valid
        float ratioA = (float)flangeA.Length / pts.Length;
        if (flangeA.Length < 50 || flangeB.Length < 50 ||
            ratioA < 0.05f || ratioA > 0.95f)
        {
            // No clear two-flange structure found – fall back to flat-plate
            return MeshBuilder.Build(pts, cellSize, thickness, smoothIter, erodeN);
        }

        // ── 3. Build each flange using its own best-fit plane ─────────────────
        var meshA = MeshBuilder.Build(flangeA, cellSize, thickness, smoothIter, erodeN);
        var meshB = MeshBuilder.Build(flangeB, cellSize, thickness, smoothIter, erodeN);

        // ── 4. Combine (vertex indices of B are offset by A's vertex count) ───
        return Combine(meshA, meshB);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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

    static MeshBuilder.Mesh Combine(MeshBuilder.Mesh a, MeshBuilder.Mesh b)
    {
        int offset = a.Vertices.Count;
        var verts = new List<float[]>(a.Vertices.Concat(b.Vertices));
        var tris  = new List<int[]>(a.Triangles.Concat(
            b.Triangles.Select(t => new[]{ t[0]+offset, t[1]+offset, t[2]+offset })));
        return new MeshBuilder.Mesh(verts, tris);
    }
}
