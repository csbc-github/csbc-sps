namespace ThreeDScan.Application.Common.Models;

/// <summary>正式面掃描是否改讀既有 ply（appsettings <c>SurfaceScan</c>；客戶現場無掃描器時用）。</summary>
public sealed class SurfaceScanSettings
{
    /// <summary>設定區段名稱。</summary>
    public const string SectionName = "SurfaceScan";

    /// <summary>是否使用既有 ply，略過掃描器。預設 false。</summary>
    public bool UseExistingPly { get; set; } = false;

    /// <summary>ply 檔絕對路徑；未設定為 null。</summary>
    public string? ExistingPlyPath { get; set; }
}
