namespace PlyProcessor.Core;

/// <summary>
/// 規格 v1.1 §9.8：外表面點雲局部平面法向量與內表面測距點的投影距離。
/// </summary>
public static class FlangeThicknessMeasurer
{
    private const float NeighborhoodRadiusMm = 20f;
    private const int MinNeighborhoodCount = 12;
    private const int FallbackNearestCount = 50;
    private const int MinPlanePoints = 3;

    /// <summary>
    /// 分別對左／右內表面點計算厚度。未提供該側或無法擬合時該側為 null。
    /// </summary>
    /// <param name="pts">角鋼外表面點雲（mm）。</param>
    /// <param name="leftX">左側測距點 X；該側未量測時為 null。</param>
    /// <param name="leftY">左側測距點 Y。</param>
    /// <param name="leftZ">左側測距點 Z。</param>
    /// <param name="rightX">右側測距點 X；該側未量測時為 null。</param>
    /// <param name="rightY">右側測距點 Y。</param>
    /// <param name="rightZ">右側測距點 Z。</param>
    /// <returns>左側／右側厚度（mm）。</returns>
    public static FlangeThicknessMeasureResult Measure(
        PlyPoint[] pts,
        float? leftX,
        float? leftY,
        float? leftZ,
        float? rightX,
        float? rightY,
        float? rightZ)
    {
        double? thicknessLeft = null;
        if (leftX.HasValue && leftY.HasValue && leftZ.HasValue)
        {
            thicknessLeft = TryMeasureSide(pts, leftX.Value, leftY.Value, leftZ.Value);
        }

        double? thicknessRight = null;
        if (rightX.HasValue && rightY.HasValue && rightZ.HasValue)
        {
            thicknessRight = TryMeasureSide(pts, rightX.Value, rightY.Value, rightZ.Value);
        }

        return new FlangeThicknessMeasureResult(thicknessLeft, thicknessRight);
    }

    /// <summary>
    /// 以內表面 XY 為中心取外表面鄰域、擬合平面，厚度為
    /// <c>|(P_outer − inner) · n|</c>（P_outer 取平面形心）。
    /// </summary>
    /// <param name="pts">外表面點雲。</param>
    /// <param name="innerX">測距點 X（mm）。</param>
    /// <param name="innerY">測距點 Y（mm）。</param>
    /// <param name="innerZ">測距點 Z（mm）。</param>
    /// <returns>厚度（mm）；點不足或法向量退化時為 null。</returns>
    public static double? TryMeasureSide(PlyPoint[] pts, float innerX, float innerY, float innerZ)
    {
        if (pts is null || pts.Length == 0)
        {
            return null;
        }

        var neighborhood = CollectNeighborhood(pts, innerX, innerY);
        if (neighborhood.Length < MinPlanePoints)
        {
            return null;
        }

        var plane = PcaFitter.Fit(neighborhood);
        double nx = plane.E3[0];
        double ny = plane.E3[1];
        double nz = plane.E3[2];
        double nLen = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        if (nLen < 1e-9)
        {
            return null;
        }

        nx /= nLen;
        ny /= nLen;
        nz /= nLen;
        double dx = plane.Cx - innerX;
        double dy = plane.Cy - innerY;
        double dz = plane.Cz - innerZ;
        return Math.Abs((dx * nx) + (dy * ny) + (dz * nz));
    }

    /// <summary>
    /// 在點雲中找出與指定 XY 歐氏距離平方最小的點索引（忽略 Z）。
    /// </summary>
    /// <param name="pts">搜尋用點雲；呼叫端須保證長度大於 0。</param>
    /// <param name="x">目標 X（mm）。</param>
    /// <param name="y">目標 Y（mm）。</param>
    /// <returns>最近點在 <paramref name="pts"/> 中的索引；距離相同時取先出現者。</returns>
    public static int FindNearestXyIndex(PlyPoint[] pts, float x, float y)
    {
        int best = 0;
        double bestD2 = double.MaxValue;
        for (int i = 0; i < pts.Length; i++)
        {
            double dx = pts[i].X - x;
            double dy = pts[i].Y - y;
            double d2 = (dx * dx) + (dy * dy);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// 以內表面 XY 為中心，先取半徑內點；不足則改取 XY 最近的一批點。
    /// </summary>
    private static PlyPoint[] CollectNeighborhood(PlyPoint[] pts, float x, float y)
    {
        double radius2 = NeighborhoodRadiusMm * NeighborhoodRadiusMm;
        var inside = new List<PlyPoint>();
        for (int i = 0; i < pts.Length; i++)
        {
            double dx = pts[i].X - x;
            double dy = pts[i].Y - y;
            if (((dx * dx) + (dy * dy)) <= radius2)
            {
                inside.Add(pts[i]);
            }
        }

        if (inside.Count >= MinNeighborhoodCount)
        {
            return inside.ToArray();
        }

        return TakeNearestXy(pts, x, y, FallbackNearestCount);
    }

    /// <summary>
    /// 依 XY 距離取最近的 <paramref name="k"/> 點。
    /// </summary>
    private static PlyPoint[] TakeNearestXy(PlyPoint[] pts, float x, float y, int k)
    {
        int take = k;
        if (take > pts.Length)
        {
            take = pts.Length;
        }

        var scored = new (double D2, int Index)[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            double dx = pts[i].X - x;
            double dy = pts[i].Y - y;
            scored[i] = ((dx * dx) + (dy * dy), i);
        }

        Array.Sort(scored, CompareDistanceThenIndex);
        var result = new PlyPoint[take];
        for (int i = 0; i < take; i++)
        {
            result[i] = pts[scored[i].Index];
        }

        return result;
    }

    private static int CompareDistanceThenIndex((double D2, int Index) a, (double D2, int Index) b)
    {
        int byDist = a.D2.CompareTo(b.D2);
        if (byDist != 0)
        {
            return byDist;
        }

        return a.Index.CompareTo(b.Index);
    }
}
