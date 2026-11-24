using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>SICK 光達錄製與截面整理。計算由 Python 工作程序執行。</summary>
public interface ISickLineScanner
{
    // 舊版 POST /api/calibration/roller 已停用。
    // /// <summary>取一段空滾輪截面並擬合平面距離。</summary>
    // Task<RollerSampleResult> SampleRollerAsync(
    //     string ip,
    //     int port,
    //     double toleranceMm,
    //     double rangeDeviationMm,
    //     int frames,
    //     CancellationToken ct = default);

    /// <summary>開始把每一圈寫進工作目錄，直到呼叫停止。</summary>
    Task StartRecordingAsync(string sessionDirectory, string ip, int port, CancellationToken ct = default);

    /// <summary>請錄製程序收尾並寫完檔案。</summary>
    Task StopRecordingAsync(string sessionDirectory, CancellationToken ct = default);

    /// <summary>立刻結束錄製程序。不保留後續整理。</summary>
    Task AbortRecordingAsync(string sessionDirectory, CancellationToken ct = default);

    /// <summary>依 Encoder 錄製與桌面兩點校正，把錄製整理成各支角鋼。不再讀取滾輪 ZDistance。</summary>
    Task<IReadOnlyList<MeasuredAngleIron>> MeasureAsync(
        string sessionDirectory,
        double gapMm,
        double minHeightMm,
        CancellationToken ct = default);
}
