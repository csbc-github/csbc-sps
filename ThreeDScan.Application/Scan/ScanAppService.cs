using ThreeDScan.Application.Calibration.Dtos;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Scan.Dtos;

namespace ThreeDScan.Application.Scan;

/// <summary>掃描應用服務；業務邏輯尚未實作。</summary>
public sealed class ScanAppService : IScanAppService
{
    /// <inheritdoc />
    public Task<ApiResponse<LineScanData>> ScanLineAsync(LineScanRequest request, CancellationToken ct = default)
    {
        _ = request;
        _ = ct;
        return Task.FromResult(SpecNotImplemented.Response<LineScanData>("POST /api/scan/line"));
    }

    /// <inheritdoc />
    public Task<ApiResponse<SurfaceScanData>> ScanSurfaceAsync(SurfaceScanRequest request, CancellationToken ct = default)
    {
        _ = request;
        _ = ct;
        return Task.FromResult(SpecNotImplemented.Response<SurfaceScanData>("POST /api/scan/surface"));
    }

    /// <inheritdoc />
    public Task<ApiResponse<object>> GetSurfaceAttachmentsAsync(string taskId, CancellationToken ct = default)
    {
        _ = taskId;
        _ = ct;
        return Task.FromResult(SpecNotImplemented.Response<object>("GET /api/scan/surface/{taskId}/attachments"));
    }

    /// <inheritdoc />
    public Task<ApiResponse<HoleScanData>> ScanHoleAsync(HoleScanRequest request, CancellationToken ct = default)
    {
        _ = request;
        _ = ct;
        return Task.FromResult(SpecNotImplemented.Response<HoleScanData>("POST /api/scan/hole"));
    }
}
