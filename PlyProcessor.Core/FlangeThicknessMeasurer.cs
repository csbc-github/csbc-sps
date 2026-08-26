namespace PlyProcessor.Core;

/// <summary>
/// 在補償後點雲上，以夾具點 A／B 的 XY 找最近鄰，再用該點 Z 減夾具 Z 得到兩翼厚度。
/// 僅噴印區現場量測使用；割切區改讀資料庫已存厚度，不呼叫本類別。
/// </summary>
public static class FlangeThicknessMeasurer
{
    /// <summary>
    /// 分別對夾具點 A、B 在點雲 XY 平面找最近點，計算
    /// <c>thickness = 該點.Z − 夾具.Z</c>。
    /// </summary>
    /// <param name="pts">處理後（通常已補償）的角鋼點雲，單位 mm。</param>
    /// <param name="ax">夾具點 A 的 X（與公開點雲同一補償框）。</param>
    /// <param name="ay">夾具點 A 的 Y。</param>
    /// <param name="az">夾具點 A 的 Z；厚度為最近點 Z 減去此值。</param>
    /// <param name="bx">夾具點 B 的 X。</param>
    /// <param name="by">夾具點 B 的 Y。</param>
    /// <param name="bz">夾具點 B 的 Z。</param>
    /// <returns>兩翼厚度（mm），可為負值（若掃描面低於夾具 Z）。</returns>
    /// <exception cref="InvalidOperationException">點雲為 null 或沒有任何點。</exception>
    public static FlangeThicknessMeasureResult Measure(
        PlyPoint[] pts,
        float ax,
        float ay,
        float az,
        float bx,
        float by,
        float bz)
    {
        if (pts is null || pts.Length == 0)
        {
            throw new InvalidOperationException("厚度量測失敗：點雲沒有可用點。");
        }

        int iA = FindNearestXyIndex(pts, ax, ay);
        int iB = FindNearestXyIndex(pts, bx, by);
        double thicknessA = pts[iA].Z - az;
        double thicknessB = pts[iB].Z - bz;
        return new FlangeThicknessMeasureResult(thicknessA, thicknessB);
    }

    /// <summary>
    /// 在點雲中找出與指定 XY 歐氏距離平方最小的點索引（忽略 Z）。
    /// 用於「同樣 XY 位置」對應掃描點。
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
            double d2 = dx * dx + dy * dy;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = i;
            }
        }

        return best;
    }
}
