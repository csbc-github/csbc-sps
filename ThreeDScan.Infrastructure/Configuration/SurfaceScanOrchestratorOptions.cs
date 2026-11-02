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
}
