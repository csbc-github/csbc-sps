namespace ThreeDScan.Domain.Models;

/// <summary>線掃描與滾輪校正設定（appsettings <c>LineScan</c>）。</summary>
public sealed class LineScanSettings
{
    /// <summary>設定區段名稱。</summary>
    public const string SectionName = "LineScan";

    /// <summary>用來執行工作程序的 Python 執行檔。</summary>
    public string PythonExe { get; set; } = "python";

    /// <summary>既有 IP_control 資料夾。錄製時載入其中的 sick_lidar。</summary>
    public string IpControlRoot { get; set; } = "";

    /// <summary>橫向分件空隙，以及同一軌中間掃不到時沿滾輪的空檔 (mm)。</summary>
    public double GapMm { get; set; } = 10;

    /// <summary>低於滾輪平面此高度的點視為滾輪，不進入角鋼 (mm)。須高於滾輪面與基準的誤差，否則滾輪面會把相鄰角鋼接成一件。</summary>
    public double MinHeightMm { get; set; } = 15;

    /// <summary>剔除離群值後要平均的圈數。1 表示不平均，最大 600。與 Python 手動掃描相同。</summary>
    public int AverageScans { get; set; } = 20;

    /// <summary>裁切開始秒。平均前丟掉此秒之前的圈。0 表示從錄製開頭留下。</summary>
    public double CropStartSeconds { get; set; } = 0;

    /// <summary>裁切結束秒。沒有值表示留到最後一圈。平均前丟掉此秒之後的圈。</summary>
    public double? CropEndSeconds { get; set; }

    /// <summary>規格 RollerFlatnessTolerance。滾輪範圍內的輪廓變異超過此值視為尚未清空 (mm)。</summary>
    public double RollerFlatnessToleranceMm { get; set; } = 5;

    /// <summary>離開滾輪平面超過此值的點視為滾輪外，例如兩側高板或外側地板 (mm)。</summary>
    public double RollerRangeDeviationMm { get; set; } = 15;

    /// <summary>空滾輪校正要平均的截面數。</summary>
    public int CalibrationFrames { get; set; } = 40;

    /// <summary>尺寸引擎。HankPython 呼叫 sick_dimensions.measure_parts；CSharp 為同一套分件的改寫。</summary>
    public string MeasurementEngine { get; set; } = EngineHankPython;

    /// <summary>完成時呼叫 Hank 的 measure_parts。</summary>
    public const string EngineHankPython = "HankPython";

    /// <summary>完成時用 C# 改寫的同一套分件。</summary>
    public const string EngineCSharp = "CSharp";
}
