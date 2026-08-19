using ThreeDScan.Application.Calibration.Dtos;
using ThreeDScan.Application.Scan.Dtos;

namespace ThreeDScan.Application.Scan;

/// <summary>線掃描、面掃描與孔洞掃描。</summary>
public interface IScanAppService
{
    /// <summary>預備進料區線掃描：滾輪傳動時持續辨識角鋼輪廓。</summary>
    Task<ApiResponse<LineScanData>> ScanLineAsync(LineScanRequest request, CancellationToken ct = default);

    /// <summary>噴印區／割切區面掃描；可分段呼叫，最後一次完成拼接。</summary>
    Task<ApiResponse<SurfaceScanData>> ScanSurfaceAsync(SurfaceScanRequest request, CancellationToken ct = default);

    /// <summary>下載面掃描附加檔（ZIP：STEP／glTF／PNG）。</summary>
    Task<ApiResponse<object>> GetSurfaceAttachmentsAsync(string taskId, CancellationToken ct = default);

    /// <summary>研磨區孔洞掃描，回傳孔洞邊界座標。</summary>
    Task<ApiResponse<HoleScanData>> ScanHoleAsync(HoleScanRequest request, CancellationToken ct = default);
}
