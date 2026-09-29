namespace PlyProcessor.Core;

/// <summary>
/// 多段點雲拼接：各段 PCA 最長軸對齊 X，再沿 X 首尾相接。
/// </summary>
public static class ScanStitcher
{
    /// <summary>
    /// 將多段點雲對齊後沿 X 排開合併。
    /// </summary>
    /// <param name="segments">各段點雲，順序即沿 X 的排列順序。</param>
    /// <param name="options">間距、裁切、Y／Z 對齊、Z 歸零；null 則用 Hank 網頁預設。</param>
    /// <returns>合併後的點雲。</returns>
    /// <exception cref="InvalidOperationException">沒有可用點。</exception>
    public static PlyPoint[] Stitch(IReadOnlyList<PlyPoint[]> segments, ScanStitchOptions? options = null)
    {
        options ??= new ScanStitchOptions();
        var prepared = new List<PlyPoint[]>();
        for (int i = 0; i < segments.Count; i++)
        {
            var pts = segments[i];
            if (pts is null || pts.Length == 0)
            {
                continue;
            }

            pts = AlignScanToXAxis(pts);
            pts = TrimScanEdges(pts, options.TrimPercent);
            if (pts.Length == 0)
            {
                continue;
            }

            prepared.Add(pts);
        }

        if (prepared.Count == 0)
        {
            throw new InvalidOperationException("拼接失敗：沒有可用的段點雲。");
        }

        if (options.AlignYZ)
        {
            AlignScansYZ(prepared);
        }

        if (options.ZFloor)
        {
            ZFloorToZero(prepared);
        }

        var offsets = ComputeStitchOffsets(prepared, options.GapMm);
        int total = 0;
        for (int i = 0; i < prepared.Count; i++)
        {
            total += prepared[i].Length;
        }

        var merged = new PlyPoint[total];
        int n = 0;
        for (int s = 0; s < prepared.Count; s++)
        {
            float xOff = offsets[s];
            var pts = prepared[s];
            for (int i = 0; i < pts.Length; i++)
            {
                var p = pts[i];
                merged[n] = new PlyPoint(p.X + xOff, p.Y, p.Z, p.Intensity);
                n++;
            }
        }

        return merged;
    }

    /// <summary>
    /// PCA：最長軸對齊 X、法線對齊 Z，重心歸零。
    /// </summary>
    private static PlyPoint[] AlignScanToXAxis(PlyPoint[] pts)
    {
        int count = pts.Length;
        if (count < 3)
        {
            return pts;
        }

        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < count; i++)
        {
            cx += pts[i].X;
            cy += pts[i].Y;
            cz += pts[i].Z;
        }

        cx /= count;
        cy /= count;
        cz /= count;

        double cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
        for (int i = 0; i < count; i++)
        {
            double dx = pts[i].X - cx;
            double dy = pts[i].Y - cy;
            double dz = pts[i].Z - cz;
            cxx += dx * dx;
            cxy += dx * dy;
            cxz += dx * dz;
            cyy += dy * dy;
            cyz += dy * dz;
            czz += dz * dz;
        }

        cxx /= count;
        cxy /= count;
        cxz /= count;
        cyy /= count;
        cyz /= count;
        czz /= count;

        var e1 = PowerIter(cxx, cxy, cxz, cyy, cyz, czz, 1, 0, 0);
        double lam1 = Eigenvalue(cxx, cxy, cxz, cyy, cyz, czz, e1);

        double d2xx = cxx - lam1 * e1[0] * e1[0];
        double d2xy = cxy - lam1 * e1[0] * e1[1];
        double d2xz = cxz - lam1 * e1[0] * e1[2];
        double d2yy = cyy - lam1 * e1[1] * e1[1];
        double d2yz = cyz - lam1 * e1[1] * e1[2];
        double d2zz = czz - lam1 * e1[2] * e1[2];

        double sv2x, sv2y, sv2z;
        if (Math.Abs(e1[0]) < 0.9)
        {
            sv2x = 0;
            sv2y = -e1[2];
            sv2z = e1[1];
        }
        else
        {
            sv2x = e1[2];
            sv2y = 0;
            sv2z = -e1[0];
        }

        NormalizeInPlace(ref sv2x, ref sv2y, ref sv2z);
        var e2 = PowerIter(d2xx, d2xy, d2xz, d2yy, d2yz, d2zz, sv2x, sv2y, sv2z);
        double lam2 = Eigenvalue(d2xx, d2xy, d2xz, d2yy, d2yz, d2zz, e2);

        double d3xx = d2xx - lam2 * e2[0] * e2[0];
        double d3xy = d2xy - lam2 * e2[0] * e2[1];
        double d3xz = d2xz - lam2 * e2[0] * e2[2];
        double d3yy = d2yy - lam2 * e2[1] * e2[1];
        double d3yz = d2yz - lam2 * e2[1] * e2[2];
        double d3zz = d2zz - lam2 * e2[2] * e2[2];

        double sv3x = e1[1] * e2[2] - e1[2] * e2[1];
        double sv3y = e1[2] * e2[0] - e1[0] * e2[2];
        double sv3z = e1[0] * e2[1] - e1[1] * e2[0];
        NormalizeInPlace(ref sv3x, ref sv3y, ref sv3z);
        var e3 = PowerIter(d3xx, d3xy, d3xz, d3yy, d3yz, d3zz, sv3x, sv3y, sv3z);

        if (e1[0] < 0)
        {
            e1[0] = -e1[0];
            e1[1] = -e1[1];
            e1[2] = -e1[2];
        }

        if (e3[2] < 0)
        {
            e3[0] = -e3[0];
            e3[1] = -e3[1];
            e3[2] = -e3[2];
        }

        e2[0] = e3[1] * e1[2] - e3[2] * e1[1];
        e2[1] = e3[2] * e1[0] - e3[0] * e1[2];
        e2[2] = e3[0] * e1[1] - e3[1] * e1[0];

        var rotated = new PlyPoint[count];
        for (int i = 0; i < count; i++)
        {
            double dx = pts[i].X - cx;
            double dy = pts[i].Y - cy;
            double dz = pts[i].Z - cz;
            float x = (float)(e1[0] * dx + e1[1] * dy + e1[2] * dz);
            float y = (float)(e2[0] * dx + e2[1] * dy + e2[2] * dz);
            float z = (float)(e3[0] * dx + e3[1] * dy + e3[2] * dz);
            rotated[i] = new PlyPoint(x, y, z, pts[i].Intensity);
        }

        return rotated;
    }

    /// <summary>依 X 範圍裁掉兩端百分比的點。</summary>
    private static PlyPoint[] TrimScanEdges(PlyPoint[] pts, float pct)
    {
        if (pct <= 0 || pts.Length == 0)
        {
            return pts;
        }

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        for (int i = 0; i < pts.Length; i++)
        {
            float x = pts[i].X;
            if (x < minX)
            {
                minX = x;
            }

            if (x > maxX)
            {
                maxX = x;
            }
        }

        float margin = (maxX - minX) * pct / 100f;
        float lo = minX + margin;
        float hi = maxX - margin;
        var kept = new List<PlyPoint>(pts.Length);
        for (int i = 0; i < pts.Length; i++)
        {
            float x = pts[i].X;
            if (x >= lo && x <= hi)
            {
                kept.Add(pts[i]);
            }
        }

        return kept.ToArray();
    }

    /// <summary>後段 Y／Z 中位數對齊第一段。</summary>
    private static void AlignScansYZ(List<PlyPoint[]> scans)
    {
        if (scans.Count < 2)
        {
            return;
        }

        float refY = MedianY(scans[0]);
        float refZ = MedianZ(scans[0]);
        for (int s = 1; s < scans.Count; s++)
        {
            float dy = refY - MedianY(scans[s]);
            float dz = refZ - MedianZ(scans[s]);
            if (Math.Abs(dy) < 0.001f && Math.Abs(dz) < 0.001f)
            {
                continue;
            }

            var pts = scans[s];
            var shifted = new PlyPoint[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var p = pts[i];
                shifted[i] = new PlyPoint(p.X, p.Y + dy, p.Z + dz, p.Intensity);
            }

            scans[s] = shifted;
        }
    }

    /// <summary>全體最低 Z 歸零。</summary>
    private static void ZFloorToZero(List<PlyPoint[]> scans)
    {
        float minZ = float.MaxValue;
        for (int s = 0; s < scans.Count; s++)
        {
            var pts = scans[s];
            for (int i = 0; i < pts.Length; i++)
            {
                if (pts[i].Z < minZ)
                {
                    minZ = pts[i].Z;
                }
            }
        }

        if (Math.Abs(minZ) < 0.001f)
        {
            return;
        }

        for (int s = 0; s < scans.Count; s++)
        {
            var pts = scans[s];
            var shifted = new PlyPoint[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var p = pts[i];
                shifted[i] = new PlyPoint(p.X, p.Y, p.Z - minZ, p.Intensity);
            }

            scans[s] = shifted;
        }
    }

    /// <summary>各段 X 平移，使包圍盒沿 X 首尾相接。</summary>
    private static float[] ComputeStitchOffsets(List<PlyPoint[]> scans, float gap)
    {
        var offsets = new float[scans.Count];
        float cursor = 0f;
        for (int s = 0; s < scans.Count; s++)
        {
            var pts = scans[s];
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            for (int i = 0; i < pts.Length; i++)
            {
                float x = pts[i].X;
                if (x < minX)
                {
                    minX = x;
                }

                if (x > maxX)
                {
                    maxX = x;
                }
            }

            offsets[s] = cursor - minX;
            cursor += (maxX - minX) + gap;
        }

        return offsets;
    }

    private static float[] PowerIter(
        double cxx, double cxy, double cxz, double cyy, double cyz, double czz,
        double vx, double vy, double vz)
    {
        for (int k = 0; k < 80; k++)
        {
            double nx = cxx * vx + cxy * vy + cxz * vz;
            double ny = cxy * vx + cyy * vy + cyz * vz;
            double nz = cxz * vx + cyz * vy + czz * vz;
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-12)
            {
                break;
            }

            vx = nx / len;
            vy = ny / len;
            vz = nz / len;
        }

        return new float[] { (float)vx, (float)vy, (float)vz };
    }

    private static double Eigenvalue(
        double cxx, double cxy, double cxz, double cyy, double cyz, double czz,
        float[] e)
    {
        double ax = cxx * e[0] + cxy * e[1] + cxz * e[2];
        double ay = cxy * e[0] + cyy * e[1] + cyz * e[2];
        double az = cxz * e[0] + cyz * e[1] + czz * e[2];
        return e[0] * ax + e[1] * ay + e[2] * az;
    }

    private static void NormalizeInPlace(ref double x, ref double y, ref double z)
    {
        double len = Math.Sqrt(x * x + y * y + z * z);
        if (len < 1e-12)
        {
            return;
        }

        x /= len;
        y /= len;
        z /= len;
    }

    private static float MedianY(PlyPoint[] pts)
    {
        var values = new float[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            values[i] = pts[i].Y;
        }

        return Median(values);
    }

    private static float MedianZ(PlyPoint[] pts)
    {
        var values = new float[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            values[i] = pts[i].Z;
        }

        return Median(values);
    }

    private static float Median(float[] values)
    {
        Array.Sort(values);
        return values[values.Length >> 1];
    }
}
