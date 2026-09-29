namespace PlyProcessor.Core;

/// <summary>
/// 從 raw_scene.ply（掃描器座標的原始點）去掉墊板／桌面，留下角鋼。
/// 掃描器 Z 較大的一側是靠近相機；點數最多的 Z 高峰通常是正對相機的墊板。
/// 擬合該平面後，依高度分布找台面群與工件群的空隙，門檻貼台面側，
/// 以盡量保留倒 V 外側近滾輪的點（包角朝上，掃到的是外表面）。
/// 找不到空隙時不切高度，整雲只去雜訊。
/// </summary>
public static class ScanProcessor
{
    public static PlyPoint[] Process(
        PlyPoint[] rawPts,
        int   minNoiseSize  = 200,
        float noiseVoxel    = 3f)
    {
        if (rawPts.Length == 0)
        {
            return rawPts;
        }

        const float plateBandMm = 10f;
        var zValues = new float[rawPts.Length];
        for (int i = 0; i < rawPts.Length; i++)
        {
            zValues[i] = rawPts[i].Z;
        }

        float zPeak = FindPeak(zValues);
        int plateCount = 0;
        for (int i = 0; i < rawPts.Length; i++)
        {
            if (MathF.Abs(rawPts[i].Z - zPeak) <= plateBandMm)
            {
                plateCount++;
            }
        }

        var filtered = new List<PlyPoint>();
        int minObjectCount = Math.Max(minNoiseSize, rawPts.Length / 200);
        if (plateCount >= minNoiseSize)
        {
            var platePts = new PlyPoint[plateCount];
            int w = 0;
            for (int i = 0; i < rawPts.Length; i++)
            {
                if (MathF.Abs(rawPts[i].Z - zPeak) <= plateBandMm)
                {
                    platePts[w] = rawPts[i];
                    w++;
                }
            }

            var plane = PcaFitter.Fit(platePts);
            var heights = ComputeHeights(rawPts, plane);
            OrientHeightsTowardHigherZ(rawPts, heights, plane.Cz);

            float? heightThresh = TryFindHeightThreshold(heights, minObjectCount);

            // 舊做法：固定 zThresh（呼叫端預設 12 mm）切高度。
            // int aboveCount = 0;
            // for (int i = 0; i < heights.Length; i++)
            // {
            //     if (heights[i] > zThresh)
            //     {
            //         aboveCount++;
            //     }
            // }
            //
            // if (aboveCount >= minObjectCount)
            // {
            //     filtered.Capacity = aboveCount;
            //     for (int i = 0; i < rawPts.Length; i++)
            //     {
            //         if (heights[i] > zThresh)
            //         {
            //             filtered.Add(rawPts[i]);
            //         }
            //     }
            // }

            if (heightThresh.HasValue)
            {
                float t = heightThresh.Value;
                int aboveCount = 0;
                for (int i = 0; i < heights.Length; i++)
                {
                    if (heights[i] > t)
                    {
                        aboveCount++;
                    }
                }

                if (aboveCount >= minObjectCount)
                {
                    filtered.Capacity = aboveCount;
                    for (int i = 0; i < rawPts.Length; i++)
                    {
                        if (heights[i] > t)
                        {
                            filtered.Add(rawPts[i]);
                        }
                    }
                }
            }
        }

        if (filtered.Count == 0)
        {
            for (int i = 0; i < rawPts.Length; i++)
            {
                filtered.Add(rawPts[i]);
            }
        }

        var clusters = VoxelClusterer.Cluster([.. filtered], "denoise", noiseVoxel, minNoiseSize);
        if (clusters.Count == 0)
        {
            throw new InvalidOperationException("去雜訊後沒有剩餘點群，請降低「最小點群」。");
        }

        return [.. clusters.SelectMany(c => c.Points)];
    }

    /// <summary>
    /// 擬合後高度直方圖：台面群（近 0）尾端即下限，門檻取該處左緣。
    /// 倒 V 兩翼外側在高度上是長平台，不要求第二高峰。找不到台面尾端或上面點太少時回傳 null。
    /// </summary>
    static float? TryFindHeightThreshold(float[] heights, int minWorkpieceCount)
    {
        const float binW = 1f;
        const float tableSearchMin = -10f;
        const float tableSearchMax = 10f;
        const float tableTailRatio = 0.1f;
        const int gapHoldBins = 2;

        float hMin = float.MaxValue;
        float hMax = float.MinValue;
        for (int i = 0; i < heights.Length; i++)
        {
            if (heights[i] < hMin)
            {
                hMin = heights[i];
            }

            if (heights[i] > hMax)
            {
                hMax = heights[i];
            }
        }

        if (hMax <= 0f || hMax - hMin < binW)
        {
            return null;
        }

        float origin = MathF.Floor(Math.Min(hMin, tableSearchMin) / binW) * binW;
        int nBins = (int)MathF.Ceiling((hMax - origin) / binW) + 1;
        if (nBins < 4)
        {
            return null;
        }

        var bins = new int[nBins];
        for (int i = 0; i < heights.Length; i++)
        {
            int b = (int)((heights[i] - origin) / binW);
            if (b < 0)
            {
                b = 0;
            }

            if (b >= nBins)
            {
                b = nBins - 1;
            }

            bins[b]++;
        }

        var smooth = new float[nBins];
        for (int b = 0; b < nBins; b++)
        {
            int i0 = b;
            if (b > 0)
            {
                i0 = b - 1;
            }

            int i2 = b;
            if (b + 1 < nBins)
            {
                i2 = b + 1;
            }

            smooth[b] = (bins[i0] + bins[b] + bins[i2]) / 3f;
        }

        int tableBin = -1;
        float tablePeak = 0f;
        for (int b = 0; b < nBins; b++)
        {
            float center = origin + (b + 0.5f) * binW;
            if (center < tableSearchMin || center > tableSearchMax)
            {
                continue;
            }

            if (tableBin < 0 || smooth[b] > tablePeak)
            {
                tableBin = b;
                tablePeak = smooth[b];
            }
        }

        if (tableBin < 0 || tablePeak < 1f)
        {
            return null;
        }

        float tailCut = tablePeak * tableTailRatio;
        int gapBin = -1;
        int lowRun = 0;
        for (int b = tableBin + 1; b < nBins; b++)
        {
            // 倒 V 兩翼外側在高度上是長平台，不是第二高峰；用原始 bin，避免平滑把台面高峰拖進近滾輪的外側點。
            if (bins[b] >= tailCut)
            {
                gapBin = -1;
                lowRun = 0;
                continue;
            }

            if (gapBin < 0)
            {
                gapBin = b;
            }

            lowRun++;
            if (lowRun >= gapHoldBins)
            {
                break;
            }
        }

        if (gapBin < 0 || lowRun < gapHoldBins)
        {
            return null;
        }

        int above = 0;
        for (int b = gapBin; b < nBins; b++)
        {
            above += bins[b];
        }

        if (above < minWorkpieceCount)
        {
            return null;
        }

        return origin + gapBin * binW;
    }

    /// <summary>
    /// 各點到 PCA 平面的有向高度（mm）。
    /// </summary>
    static float[] ComputeHeights(PlyPoint[] pts, PcaPlane plane)
    {
        var heights = new float[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            float dx = pts[i].X - plane.Cx;
            float dy = pts[i].Y - plane.Cy;
            float dz = pts[i].Z - plane.Cz;
            heights[i] = dx * plane.E3[0] + dy * plane.E3[1] + dz * plane.E3[2];
        }

        return heights;
    }

    /// <summary>
    /// PCA 法線可正可負。以高度與（Z − 平面中心 Z）的共變數決定是否翻轉，
    /// 使正高度對應較大的掃描器 Z（墊板前方的物件）。
    /// </summary>
    static void OrientHeightsTowardHigherZ(PlyPoint[] pts, float[] heights, float planeCz)
    {
        double corr = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            corr += heights[i] * (pts[i].Z - planeCz);
        }

        if (corr >= 0)
        {
            return;
        }

        for (int i = 0; i < heights.Length; i++)
        {
            heights[i] = -heights[i];
        }
    }

    /// <summary>
    /// 一維直方圖計數最高的 bin 中心。
    /// </summary>
    static float FindPeak(float[] values)
    {
        float vMin = float.MaxValue;
        float vMax = float.MinValue;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < vMin)
            {
                vMin = values[i];
            }

            if (values[i] > vMax)
            {
                vMax = values[i];
            }
        }

        const int nBins = 80;
        float range = vMax - vMin;
        if (range < 1e-4f)
        {
            return vMin;
        }

        float binW = range / nBins;
        var bins = new int[nBins];
        for (int i = 0; i < values.Length; i++)
        {
            int b = (int)((values[i] - vMin) / binW);
            if (b < 0)
            {
                b = 0;
            }

            if (b >= nBins)
            {
                b = nBins - 1;
            }

            bins[b]++;
        }

        int peakBin = 0;
        for (int b = 1; b < nBins; b++)
        {
            if (bins[b] > bins[peakBin])
            {
                peakBin = b;
            }
        }

        return vMin + (peakBin + 0.5f) * binW;
    }
}
