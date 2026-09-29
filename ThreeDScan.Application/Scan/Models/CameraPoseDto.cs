using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>掃描器絕對位置與姿態 {x,y,z,a,c}。</summary>
public sealed class CameraPoseDto
{
    /// <summary>X 軸座標 (mm)。</summary>
    [Range(-10000, 10000)]
    public double X { get; set; }

    /// <summary>Y 軸座標 (mm)。</summary>
    [Range(-10000, 10000)]
    public double Y { get; set; }

    /// <summary>Z 軸座標 (mm)。</summary>
    [Range(-10000, 10000)]
    public double Z { get; set; }

    /// <summary>姿態 a（軸向／單位待確認）。</summary>
    public double A { get; set; }

    /// <summary>姿態 c（軸向／單位待確認）。</summary>
    public double C { get; set; }
}
