using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Common.Models;

/// <summary>三維座標 (mm)，用於掃描器預期位置或絕對座標。</summary>
public sealed class CoordinateDto
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
}
