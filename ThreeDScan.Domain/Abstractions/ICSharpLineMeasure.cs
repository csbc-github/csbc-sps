using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>與 Hank <c>sick_dimensions.measure_parts</c> 相同的分件，用 C# 計算。</summary>
public interface ICSharpLineMeasure
{
    /// <summary>
    /// 依滾輪平面與速度把截面收成點，再沿 X 空隙分件。
    /// 先把連續圈剔除離群後平均，再套框選。只有高過 <paramref name="minHeightMm" /> 的點參與分件，
    /// 貼著滾輪的翼尖不會把相鄰兩件接起來。尺寸取中段截面的中位數，高度扣當地滾輪面。
    /// </summary>
    /// <param name="minHeightMm">分件門檻。高出當地滾輪面不足此值的截面不採用 (mm)。</param>
    /// <param name="averageScans">要平均的圈數。1 表示不平均。</param>
    /// <param name="cropStartSeconds">裁切開始秒。此秒之前的圈不進入平均。</param>
    /// <param name="cropEndSeconds">裁切結束秒。沒有值表示留到最後一圈。</param>
    IReadOnlyList<MeasuredAngleIron> Measure(
        string sessionDirectory,
        RollerBaselineRecord baseline,
        double rollerSpeedMmPerSec,
        double gapMm,
        double minHeightMm,
        LineScanRegion? region,
        int averageScans,
        double cropStartSeconds,
        double? cropEndSeconds);
}
