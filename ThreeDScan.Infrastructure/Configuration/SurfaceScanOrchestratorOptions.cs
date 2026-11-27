namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>面／孔洞正式路徑協調器選項（綁定 appsettings <c>SurfaceScan</c> 子集）。</summary>
public sealed class SurfaceScanOrchestratorOptions
{
    /// <summary>設定區段名稱（與 Application <c>SurfaceScanSettings</c> 相同）。</summary>
    public const string SectionName = "SurfaceScan";

    /// <summary>
    /// true：Surface／Hole 建任務前須 <c>Coordinates.IsCalibrated</c>（Hank <c>RequireCalibratedCoordinates</c>）。
    /// 預設 false，與既有現場相容。
    /// </summary>
    public bool RequireCalibratedCoordinates { get; set; }

    /// <summary>true：完整翼緣也須驗證成功；false：對齊成功時可保留翼緣警告並完成點雲。</summary>
    public bool RequireVerifiedStitchBoundaries { get; set; } = true;

    /// <summary>後續拼接模型預設修補過大外緣缺口；內孔與原始 PLY 保留，補入幾何另行標記。</summary>
    public bool FillOuterEdges { get; set; } = true;

    /// <summary>外緣缺口深度超過此值才修補（mm）；預設 10 mm，0 為完整外框補平。</summary>
    public double OuterEdgeRepairMinDepthMm { get; set; } = 10;
}
