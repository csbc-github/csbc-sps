namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>抽取用地面／邊緣校正（僅供物件分離，不是物理外參）。</summary>
/// <remarks>由 Hank Scan3D 的 ScanCalibration 搬入，邏輯不改。</remarks>
public sealed class ScanCalibration
{
    /// <summary>建立校正結果。</summary>
    /// <param name="edgeXmm">偵測到的 X 邊緣。</param>
    /// <param name="edgeYmm">偵測到的 Y 邊緣。</param>
    /// <param name="edgeBasisApplied">是否套用邊緣基底。</param>
    /// <param name="edgeFallbackReason">邊緣備援原因。</param>
    /// <param name="origin">基底原點。</param>
    /// <param name="xAxis">X 軸。</param>
    /// <param name="yAxis">Y 軸。</param>
    /// <param name="zAxis">Z 軸。</param>
    /// <param name="groundA">地面平面 A。</param>
    /// <param name="groundB">地面平面 B。</param>
    /// <param name="groundC">地面平面 C。</param>
    /// <param name="zZeroMm">Z 零點（mm）。</param>
    /// <param name="groundMethod">地面方法名稱。</param>
    /// <param name="cornerPointCounts">四角點數。</param>
    /// <param name="groundSampleCount">地面取樣數。</param>
    /// <param name="groundResidualRmseMm">地面殘差 RMS（mm）。</param>
    /// <param name="alignedBounds">對齊後包圍盒。</param>
    public ScanCalibration(
        double edgeXmm,
        double edgeYmm,
        bool edgeBasisApplied,
        string? edgeFallbackReason,
        ScanVector origin,
        ScanVector xAxis,
        ScanVector yAxis,
        ScanVector zAxis,
        double groundA,
        double groundB,
        double groundC,
        double zZeroMm,
        string groundMethod,
        int[] cornerPointCounts,
        int groundSampleCount,
        double groundResidualRmseMm,
        ScanBounds alignedBounds)
    {
        EdgeXmm = edgeXmm;
        EdgeYmm = edgeYmm;
        EdgeBasisApplied = edgeBasisApplied;
        EdgeFallbackReason = edgeFallbackReason;
        Origin = origin;
        XAxis = xAxis;
        YAxis = yAxis;
        ZAxis = zAxis;
        GroundA = groundA;
        GroundB = groundB;
        GroundC = groundC;
        ZZeroMm = zZeroMm;
        GroundMethod = groundMethod;
        CornerPointCounts = cornerPointCounts;
        GroundSampleCount = groundSampleCount;
        GroundResidualRmseMm = groundResidualRmseMm;
        AlignedBounds = alignedBounds;
    }

    /// <summary>偵測到的 X 邊緣。</summary>
    public double EdgeXmm { get; init; }

    /// <summary>偵測到的 Y 邊緣。</summary>
    public double EdgeYmm { get; init; }

    /// <summary>是否套用邊緣基底。</summary>
    public bool EdgeBasisApplied { get; init; }

    /// <summary>邊緣備援原因。</summary>
    public string? EdgeFallbackReason { get; init; }

    /// <summary>基底原點。</summary>
    public ScanVector Origin { get; init; }

    /// <summary>X 軸。</summary>
    public ScanVector XAxis { get; init; }

    /// <summary>Y 軸。</summary>
    public ScanVector YAxis { get; init; }

    /// <summary>Z 軸。</summary>
    public ScanVector ZAxis { get; init; }

    /// <summary>地面平面 A。</summary>
    public double GroundA { get; init; }

    /// <summary>地面平面 B。</summary>
    public double GroundB { get; init; }

    /// <summary>地面平面 C。</summary>
    public double GroundC { get; init; }

    /// <summary>Z 零點（mm）。</summary>
    public double ZZeroMm { get; init; }

    /// <summary>地面方法名稱。</summary>
    public string GroundMethod { get; init; }

    /// <summary>四角點數。</summary>
    public int[] CornerPointCounts { get; init; }

    /// <summary>地面取樣數。</summary>
    public int GroundSampleCount { get; init; }

    /// <summary>地面殘差 RMS（mm）。</summary>
    public double GroundResidualRmseMm { get; init; }

    /// <summary>對齊後包圍盒。</summary>
    public ScanBounds AlignedBounds { get; init; }

    /// <summary>原版後端校正：先換基底再減平面（仿射剪切）。</summary>
    /// <param name="raw">原始掃描器座標。</param>
    public ScanVector Transform(ScanVector raw)
    {
        var r = raw - Origin;
        double x = r.Dot(XAxis);
        double y = r.Dot(YAxis);
        double z = r.Dot(ZAxis);
        return new ScanVector(x, y, z - (GroundA * x + GroundB * y + GroundC) - ZZeroMm);
    }

    /// <summary>校正座標還原為掃描器座標。</summary>
    /// <param name="corrected">校正後座標。</param>
    public ScanVector ToScanner(ScanVector corrected)
    {
        return Origin + XAxis.Scale(corrected.X) + YAxis.Scale(corrected.Y)
            + ZAxis.Scale(corrected.Z + GroundA * corrected.X + GroundB * corrected.Y + GroundC + ZZeroMm);
    }

    /// <summary>原始→校正 4×4。</summary>
    public double[][] RawToCalibratedMatrix
    {
        get
        {
            var z = ZAxis - XAxis.Scale(GroundA) - YAxis.Scale(GroundB);
            return
            [
                [XAxis.X, XAxis.Y, XAxis.Z, -XAxis.Dot(Origin)],
                [YAxis.X, YAxis.Y, YAxis.Z, -YAxis.Dot(Origin)],
                [z.X, z.Y, z.Z, -z.Dot(Origin) - GroundC - ZZeroMm],
                [0, 0, 0, 1],
            ];
        }
    }
}
