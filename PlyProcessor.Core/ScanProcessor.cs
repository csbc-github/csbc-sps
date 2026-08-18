namespace PlyProcessor.Core;

/// <summary>
/// Replicates what the bundled Python backend does server-side:
///   1. PCA-fit the dominant flat surface (the scan plate / table ground).
///   2. Compute each point's height above that plane.
///   3. Keep only points whose height exceeds zThresh (in mm).
///   4. Remove small isolated clusters (noise) via voxel BFS.
/// The result is equivalent to the backend's "*_angle_steel.ply" output.
/// </summary>
public static class ScanProcessor
{
    public static PlyPoint[] Process(
        PlyPoint[] rawPts,
        float zThresh       = 12f,
        int   minNoiseSize  = 200,
        float noiseVoxel    = 3f)
    {
        if (rawPts.Length == 0) return rawPts;

        // 1. Fit the dominant flat surface (background plate)
        var plane = PcaFitter.Fit(rawPts);

        // 2. Height above the fitted plane for every point
        var heights = new float[rawPts.Length];
        for (int i = 0; i < rawPts.Length; i++)
        {
            float dx = rawPts[i].X - plane.Cx;
            float dy = rawPts[i].Y - plane.Cy;
            float dz = rawPts[i].Z - plane.Cz;
            heights[i] = dx*plane.E3[0] + dy*plane.E3[1] + dz*plane.E3[2];
        }

        // 3. Decide sign: PCA normal can point either way; pick the direction
        //    where more points sit above zThresh (those are the target objects).
        int posAbove = 0, negAbove = 0;
        foreach (var h in heights)
        {
            if ( h > zThresh) posAbove++;
            if (-h > zThresh) negAbove++;
        }
        float sign = negAbove > posAbove ? -1f : 1f;

        var filtered = new List<PlyPoint>(rawPts.Length / 10);
        for (int i = 0; i < rawPts.Length; i++)
            if (heights[i] * sign > zThresh)
                filtered.Add(rawPts[i]);

        if (filtered.Count == 0)
            throw new InvalidOperationException(
                $"z_thresh={zThresh} mm 過濾後沒有剩餘點（共 {rawPts.Length:N0} 點）。請嘗試降低 z_thresh。");

        // 4. Remove small isolated clusters (scanner edge noise)
        var clusters = VoxelClusterer.Cluster([.. filtered], "denoise", noiseVoxel, minNoiseSize);
        if (clusters.Count == 0)
            throw new InvalidOperationException("去雜訊後沒有剩餘點群，請降低「最小點群」或「z 最小」。");

        return [.. clusters.SelectMany(c => c.Points)];
    }
}
