namespace PlyProcessor.Core;

/// <summary>
/// 從本段角鋼點雲取前緣三點與包角線最大 Z 差。
/// 前進方向為靠近掃描器 XY (0,0) 的一端；左／右為面朝該方向、Z 朝上的左與右。
/// 薄片只用來認倒 V 截面；對方 1 mm 是特徵點相對真實外脊／翼尖的容許誤差，不是薄片厚度。
/// </summary>
public static class HeadFeatureExtractor
{
    public const string KindAngle = "angle";
    public const string KindEnvelope = "envelope";

    const float SlabMm = 10f;
    const float AccuracyMm = 1f;
    const int MinSidePoints = 20;
    const float MinVHeightMm = 8f;
    const float RidgeBinMm = 10f;
    const int MinRidgeBinPoints = 5;
    const float MinRidgeBinCountFraction = 0.2f;

    /// <summary>
    /// 在掃描器座標上計算。點太少時回傳 null。
    /// </summary>
    public static HeadFeatureExtractResult? Extract(PlyPoint[] pts)
    {
        if (pts is null || pts.Length < 3)
        {
            return null;
        }

        GetXyLengthAxis(pts, out float lx, out float ly);
        float sMin = float.MaxValue;
        float sMax = float.MinValue;
        var sValues = new float[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            float s = (pts[i].X * lx) + (pts[i].Y * ly);
            sValues[i] = s;
            if (s < sMin)
            {
                sMin = s;
            }

            if (s > sMax)
            {
                sMax = s;
            }
        }

        bool frontIsMin = FrontIsSmallerS(pts, sValues, sMin, sMax);
        float sFront = frontIsMin ? sMin : sMax;
        float fx = frontIsMin ? -lx : lx;
        float fy = frontIsMin ? -ly : ly;
        float leftX = -fy;
        float leftY = fx;

        var slab = CollectSlab(pts, sValues, sFront, SlabMm);
        if (slab.Count < 3)
        {
            return null;
        }

        PlyPoint apex = PickApex(slab, lx, ly, sFront);
        var leftPts = new List<PlyPoint>();
        var rightPts = new List<PlyPoint>();
        for (int i = 0; i < slab.Count; i++)
        {
            float dx = slab[i].X - apex.X;
            float dy = slab[i].Y - apex.Y;
            if (dx == 0f && dy == 0f && slab[i].Z == apex.Z)
            {
                continue;
            }

            float side = (dx * leftX) + (dy * leftY);
            if (side >= 0)
            {
                leftPts.Add(slab[i]);
            }
            else
            {
                rightPts.Add(slab[i]);
            }
        }

        PlyPoint left;
        if (leftPts.Count > 0)
        {
            left = PickFlangeTip(leftPts, leftX, leftY, true);
        }
        else
        {
            left = PickFlangeTip(slab, leftX, leftY, true);
        }

        PlyPoint right;
        if (rightPts.Count > 0)
        {
            right = PickFlangeTip(rightPts, leftX, leftY, false);
        }
        else
        {
            right = PickFlangeTip(slab, leftX, leftY, false);
        }

        string kind = KindEnvelope;
        if (leftPts.Count >= MinSidePoints
            && rightPts.Count >= MinSidePoints
            && (apex.Z - left.Z) >= MinVHeightMm
            && (apex.Z - right.Z) >= MinVHeightMm)
        {
            kind = KindAngle;
        }

        float ridgeDelta = ComputeRidgeZDelta(pts, sValues, sMin, sMax);
        return new HeadFeatureExtractResult
        {
            Kind = kind,
            LeftX = left.X,
            LeftY = left.Y,
            LeftZ = left.Z,
            ApexX = apex.X,
            ApexY = apex.Y,
            ApexZ = apex.Z,
            RightX = right.X,
            RightY = right.Y,
            RightZ = right.Z,
            RidgeZDelta = ridgeDelta,
            SpanMm = sMax - sMin,
        };
    }

    static void GetXyLengthAxis(PlyPoint[] pts, out float lx, out float ly)
    {
        double mx = 0;
        double my = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            mx += pts[i].X;
            my += pts[i].Y;
        }

        mx /= pts.Length;
        my /= pts.Length;
        double xx = 0;
        double xy = 0;
        double yy = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            double dx = pts[i].X - mx;
            double dy = pts[i].Y - my;
            xx += dx * dx;
            xy += dx * dy;
            yy += dy * dy;
        }

        double disc = ((xx - yy) * (xx - yy)) + (4.0 * xy * xy);
        if (disc < 0)
        {
            disc = 0;
        }

        double sqrtDisc = Math.Sqrt(disc);
        double lambda = 0.5 * (xx + yy + sqrtDisc);
        double evx = xy;
        double evy = lambda - xx;
        double evLen = Math.Sqrt((evx * evx) + (evy * evy));
        if (evLen < 1e-9)
        {
            evx = lambda - yy;
            evy = xy;
            evLen = Math.Sqrt((evx * evx) + (evy * evy));
        }

        if (evLen < 1e-9)
        {
            lx = 1f;
            ly = 0f;
            return;
        }

        lx = (float)(evx / evLen);
        ly = (float)(evy / evLen);
    }

    static bool FrontIsSmallerS(PlyPoint[] pts, float[] sValues, float sMin, float sMax)
    {
        double xMin = 0;
        double yMin = 0;
        int nMin = 0;
        double xMax = 0;
        double yMax = 0;
        int nMax = 0;
        float half = Math.Max(1f, (sMax - sMin) * 0.05f);
        for (int i = 0; i < pts.Length; i++)
        {
            if (sValues[i] <= sMin + half)
            {
                xMin += pts[i].X;
                yMin += pts[i].Y;
                nMin++;
            }

            if (sValues[i] >= sMax - half)
            {
                xMax += pts[i].X;
                yMax += pts[i].Y;
                nMax++;
            }
        }

        if (nMin == 0 || nMax == 0)
        {
            return true;
        }

        xMin /= nMin;
        yMin /= nMin;
        xMax /= nMax;
        yMax /= nMax;
        double dMin = (xMin * xMin) + (yMin * yMin);
        double dMax = (xMax * xMax) + (yMax * yMax);
        return dMin <= dMax;
    }

    static List<PlyPoint> CollectSlab(PlyPoint[] pts, float[] sValues, float sFront, float slabMm)
    {
        var list = new List<PlyPoint>();
        for (int i = 0; i < pts.Length; i++)
        {
            if (MathF.Abs(sValues[i] - sFront) <= slabMm)
            {
                list.Add(pts[i]);
            }
        }

        return list;
    }

    /// <summary>
    /// 外脊：先取最高 Z，再在 1 mm 高度帶內取最靠近前緣的點。
    /// </summary>
    static PlyPoint PickApex(List<PlyPoint> slab, float lx, float ly, float sFront)
    {
        int maxI = 0;
        for (int i = 1; i < slab.Count; i++)
        {
            if (slab[i].Z > slab[maxI].Z)
            {
                maxI = i;
            }
        }

        float maxZ = slab[maxI].Z;
        int best = maxI;
        float bestDs = MathF.Abs(((slab[maxI].X * lx) + (slab[maxI].Y * ly)) - sFront);
        for (int i = 0; i < slab.Count; i++)
        {
            if (slab[i].Z < maxZ - AccuracyMm)
            {
                continue;
            }

            float s = (slab[i].X * lx) + (slab[i].Y * ly);
            float ds = MathF.Abs(s - sFront);
            if (ds < bestDs)
            {
                bestDs = ds;
                best = i;
            }
        }

        return slab[best];
    }

    /// <summary>
    /// 翼尖：該側橫向最外側，再在 1 mm 橫向帶內取最低 Z（近滾輪的外尖，避開內側斜面）。
    /// </summary>
    static PlyPoint PickFlangeTip(List<PlyPoint> sidePts, float leftX, float leftY, bool isLeft)
    {
        float sign = isLeft ? 1f : -1f;
        float bestLat = float.MinValue;
        for (int i = 0; i < sidePts.Count; i++)
        {
            float lat = sign * ((sidePts[i].X * leftX) + (sidePts[i].Y * leftY));
            if (lat > bestLat)
            {
                bestLat = lat;
            }
        }

        int best = 0;
        float bestZ = float.MaxValue;
        bool any = false;
        for (int i = 0; i < sidePts.Count; i++)
        {
            float lat = sign * ((sidePts[i].X * leftX) + (sidePts[i].Y * leftY));
            if (lat < bestLat - AccuracyMm)
            {
                continue;
            }

            if (!any || sidePts[i].Z < bestZ)
            {
                any = true;
                bestZ = sidePts[i].Z;
                best = i;
            }
        }

        return sidePts[best];
    }

    static float ComputeRidgeZDelta(PlyPoint[] pts, float[] sValues, float sMin, float sMax)
    {
        float span = sMax - sMin;
        if (span < 1f)
        {
            return 0f;
        }

        int nBins = (int)MathF.Ceiling(span / RidgeBinMm);
        if (nBins < 2)
        {
            nBins = 2;
        }

        var maxZ = new float[nBins];
        var count = new int[nBins];
        for (int b = 0; b < nBins; b++)
        {
            maxZ[b] = float.MinValue;
        }

        for (int i = 0; i < pts.Length; i++)
        {
            int b = (int)((sValues[i] - sMin) / RidgeBinMm);
            if (b < 0)
            {
                b = 0;
            }

            if (b >= nBins)
            {
                b = nBins - 1;
            }

            count[b]++;
            if (pts[i].Z > maxZ[b])
            {
                maxZ[b] = pts[i].Z;
            }
        }

        var occupied = new int[nBins];
        int occupiedN = 0;
        for (int b = 0; b < nBins; b++)
        {
            if (count[b] < MinRidgeBinPoints)
            {
                continue;
            }

            occupied[occupiedN] = count[b];
            occupiedN++;
        }

        if (occupiedN < 2)
        {
            return 0f;
        }

        int median = MedianOccupiedCount(occupied, occupiedN);
        int minCount = (int)(median * MinRidgeBinCountFraction);
        if (minCount < MinRidgeBinPoints)
        {
            minCount = MinRidgeBinPoints;
        }

        float zLo = float.MaxValue;
        float zHi = float.MinValue;
        int used = 0;
        for (int b = 0; b < nBins; b++)
        {
            if (count[b] < minCount)
            {
                continue;
            }

            used++;
            if (maxZ[b] < zLo)
            {
                zLo = maxZ[b];
            }

            if (maxZ[b] > zHi)
            {
                zHi = maxZ[b];
            }
        }

        if (used < 2)
        {
            return 0f;
        }

        return zHi - zLo;
    }

    static int MedianOccupiedCount(int[] values, int n)
    {
        Array.Sort(values, 0, n);
        if ((n & 1) == 1)
        {
            return values[n / 2];
        }

        return (values[(n / 2) - 1] + values[n / 2]) / 2;
    }
}
