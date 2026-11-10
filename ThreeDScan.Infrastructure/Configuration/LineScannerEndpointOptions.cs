namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>單台光達的 appsettings（綁定 LineScanners:{scannerId}，對齊 Hank ScanApiScannerOptions）。</summary>
public sealed class LineScannerEndpointOptions
{
    /// <summary>光達 IP（Hank Host）。</summary>
    public string Ip { get; set; } = "192.168.0.2";

    /// <summary>CoLa B 埠。</summary>
    public int Port { get; set; } = 2111;

    /// <summary>Navigator 掃描編號。</summary>
    public int NavigatorScanNo { get; set; } = 1;

    /// <summary>定位來源；正式線掃描須 encoder。</summary>
    public string PositionSource { get; set; } = "encoder";

    /// <summary>滾輪半徑 (mm)。</summary>
    public double WheelRadiusMm { get; set; } = 100;

    /// <summary>編碼器每圈計數。</summary>
    public int EncoderCountsPerRev { get; set; } = 1024;

    /// <summary>編碼器通道（1-based）。</summary>
    public int EncoderChannel { get; set; } = 1;

    /// <summary>Encoder 正方向。</summary>
    public int Direction { get; set; } = 1;

    /// <summary>輪廓平均圈數。</summary>
    public int AverageScans { get; set; } = 20;

    /// <summary>校正後 X 下限。</summary>
    public double Xmin { get; set; } = -1000;

    /// <summary>校正後 X 上限。</summary>
    public double Xmax { get; set; } = 1000;

    /// <summary>最低有效高度 (mm)。</summary>
    public double MinHeight { get; set; } = 3;

    /// <summary>最高有效高度 (mm)。</summary>
    public double MaxHeight { get; set; } = 500;

    /// <summary>滾輸清空容許 (mm)。</summary>
    public double RollerClearanceToleranceMm { get; set; } = 3;

    /// <summary>TCP 連線逾時（秒）。</summary>
    public double ConnectionTimeoutSeconds { get; set; } = 10;
}
