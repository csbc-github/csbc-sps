namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>端部座標框。</summary>
/// <remarks>完整 XYZ 正交基底；平面係數以此框的局部座標表示。</remarks>
public sealed class EndFrame
{
    /// <summary>建立端部框。</summary>
    /// <param name="origin">原點。</param>
    /// <param name="crossAxis">橫軸。</param>
    /// <param name="longAxis">長軸。</param>
    /// <param name="upAxis">上軸。</param>
    /// <param name="leftSurface">左翼平面。</param>
    /// <param name="rightSurface">右翼平面。</param>
    /// <param name="planeNormalAngleDegrees">兩翼法線夾角（度）。</param>
    /// <param name="endpointYmm">端部 Y（mm）。</param>
    public EndFrame(
        ScanVector origin,
        ScanVector crossAxis,
        ScanVector longAxis,
        ScanVector upAxis,
        FittedSurface leftSurface,
        FittedSurface rightSurface,
        double planeNormalAngleDegrees,
        double endpointYmm)
    {
        Origin = origin;
        CrossAxis = crossAxis;
        LongAxis = longAxis;
        UpAxis = upAxis;
        LeftSurface = leftSurface;
        RightSurface = rightSurface;
        PlaneNormalAngleDegrees = planeNormalAngleDegrees;
        EndpointYmm = endpointYmm;
    }

    /// <summary>原點。</summary>
    public ScanVector Origin { get; init; }

    /// <summary>橫軸。</summary>
    public ScanVector CrossAxis { get; init; }

    /// <summary>長軸。</summary>
    public ScanVector LongAxis { get; init; }

    /// <summary>上軸。</summary>
    public ScanVector UpAxis { get; init; }

    /// <summary>左翼平面；局部 z = Ax + By + C，RMS 為原始歐氏正交距離。</summary>
    public FittedSurface LeftSurface { get; init; }

    /// <summary>右翼平面；局部 z = Ax + By + C，RMS 為原始歐氏正交距離。</summary>
    public FittedSurface RightSurface { get; init; }

    /// <summary>兩翼法線夾角（度）。</summary>
    public double PlaneNormalAngleDegrees { get; init; }

    /// <summary>平面係數使用的座標框。</summary>
    public string SurfaceCoordinateFrame => "EndFrameLocalXYZ";

    /// <summary>端點原始掃描器 Y（mm）；僅保留報告相容，不用來判定長向距離。</summary>
    public double EndpointYmm { get; init; }

    /// <summary>點對齊到端部框。</summary>
    /// <param name="p">點。</param>
    public ScanVector Align(ScanVector p)
    {
        var d = p - Origin;
        return new ScanVector(d.Dot(CrossAxis), d.Dot(LongAxis), d.Dot(UpAxis));
    }
}
