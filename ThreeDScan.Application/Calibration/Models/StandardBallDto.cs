using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Calibration.Models;

/// <summary>標準球已知絕對座標（台船傳入）。</summary>
public sealed class StandardBallDto
{
    /// <summary>球識別碼（英數與連字號）。</summary>
    [Required, StringLength(16, MinimumLength = 1)]
    [RegularExpression(@"^[A-Z0-9-]+$")]
    public string BallId { get; set; } = "";

    /// <summary>已知絕對座標 X (mm)。</summary>
    [Range(-10000, 10000)]
    public double KnownX { get; set; }

    /// <summary>已知絕對座標 Y (mm)。</summary>
    [Range(-10000, 10000)]
    public double KnownY { get; set; }

    /// <summary>已知絕對座標 Z (mm)。</summary>
    [Range(-10000, 10000)]
    public double KnownZ { get; set; }
}
