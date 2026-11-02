namespace ThreeDScan.Domain.Models;

/// <summary>角鋼頭／尾三點。對應 Hank <c>Scan3D.Processing.HeadFeatures</c>。</summary>
public sealed class HeadFeatures
{
    /// <summary>左角。</summary>
    public Point3 LeftCorner { get; init; } = new Point3();

    /// <summary>稜線端點。</summary>
    public Point3 Apex { get; init; } = new Point3();

    /// <summary>右角。</summary>
    public Point3 RightCorner { get; init; } = new Point3();
}
