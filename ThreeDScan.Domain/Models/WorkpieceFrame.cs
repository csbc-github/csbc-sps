namespace ThreeDScan.Domain.Models;

/// <summary>工件座標框。對應 Hank <c>Scan3D.Processing.WorkpieceFrame</c>。</summary>
public sealed class WorkpieceFrame
{
    /// <summary>Pose5 近似。</summary>
    public Pose5 Pose { get; init; } = new Pose5();

    /// <summary>原點。</summary>
    public Point3 Origin { get; init; } = new Point3();

    /// <summary>X 軸。</summary>
    public Point3 XAxis { get; init; } = new Point3();

    /// <summary>Y 軸。</summary>
    public Point3 YAxis { get; init; } = new Point3();

    /// <summary>Z 軸。</summary>
    public Point3 ZAxis { get; init; } = new Point3();

    /// <summary>Pose5 近似殘差（度）。</summary>
    public double Pose5ResidualDegrees { get; init; }

    /// <summary>慣例說明。</summary>
    public string Convention { get; init; } = "";
}
