using Microsoft.AspNetCore.Mvc;
using ThreeDScan.Api.Common;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;

namespace ThreeDScan.Api.Controllers;

/// <summary>線掃描、面掃描與孔洞掃描。</summary>
[ApiController]
[Route("api/scan")]
public sealed class ScanController : ControllerBase
{
    private readonly IScanAppService _scan;

    /// <summary>建立掃描 API。</summary>
    public ScanController(IScanAppService scan)
    {
        _scan = scan;
    }

    /// <summary>預備進料區線掃描：滾輪傳動時持續辨識角鋼輪廓。</summary>
    [HttpPost("line")]
    public async Task<IActionResult> ScanLine([FromBody] LineScanRequest request, CancellationToken ct)
    {
        var result = await _scan.ScanLineAsync(request, ct);
        return SpecApiActionResult.From(result);
    }

    /// <summary>開始線掃描（指南 v1.1）。</summary>
    [HttpPost("line/start")]
    public async Task<IActionResult> StartLineScan([FromBody] LineScanRequest request, CancellationToken ct)
    {
        var result = await _scan.StartLineScanAsync(request, ct);
        return DasApiActionResult.From(result);
    }

    /// <summary>結束線掃描（指南 v1.1）。接受後以 callback／GET 查詢結果。</summary>
    [HttpPost("line/complete")]
    public async Task<IActionResult> CompleteLineScan([FromBody] LineScanCompleteRequest request, CancellationToken ct)
    {
        var result = await _scan.CompleteLineScanAsync(request, ct);
        return DasApiActionResult.Accepted(result);
    }

    /// <summary>中止線掃描（指南 v1.1）。</summary>
    [HttpPost("line/abort")]
    public async Task<IActionResult> AbortLineScan([FromBody] LineScanAbortRequest request, CancellationToken ct)
    {
        var result = await _scan.AbortLineScanAsync(request, ct);
        return DasApiActionResult.From(result);
    }

    /// <summary>面掃描。</summary>
    [HttpPost("surface")]
    public async Task<IActionResult> ScanSurface([FromBody] SurfaceScanRequest request, CancellationToken ct)
    {
        var result = await _scan.ScanSurfaceAsync(request, ct);
        return SpecApiActionResult.From(result);
    }

    /// <summary>略過掃描器擷取，用既有 raw_scene.ply 跑與面掃描相同的後續處理。</summary>
    [HttpPost("surface-from-file")]
    public async Task<IActionResult> ScanSurfaceFromFile([FromBody] SurfaceFromFileRequest request, CancellationToken ct)
    {
        var result = await _scan.ScanSurfaceFromFileAsync(request, ct);
        return SpecApiActionResult.From(result);
    }

    /// <summary>開始面掃描第一段（指南 v1.1）。</summary>
    [HttpPost("surface/start")]
    public async Task<IActionResult> StartSurfaceScan([FromBody] SurfaceScanStartRequest request, CancellationToken ct)
    {
        var result = await _scan.StartSurfaceScanAsync(request, ct);
        return DasApiActionResult.From(result);
    }

    /// <summary>面掃描後續分段（指南 v1.1）。</summary>
    [HttpPost("surface/segment")]
    public async Task<IActionResult> SegmentSurfaceScan([FromBody] SurfaceScanSegmentRequest request, CancellationToken ct)
    {
        var result = await _scan.SegmentSurfaceScanAsync(request, ct);
        return DasApiActionResult.From(result);
    }

    /// <summary>結束面掃描（指南 v1.1）。即使只掃一段也須呼叫；接受後以 callback／GET 查詢結果。</summary>
    [HttpPost("surface/complete")]
    public async Task<IActionResult> CompleteSurfaceScan(
        [FromBody] SurfaceScanCompleteRequest request,
        CancellationToken ct)
    {
        var result = await _scan.CompleteSurfaceScanAsync(request, ct);
        return DasApiActionResult.Accepted(result);
    }

    /// <summary>中止面掃描（指南 v1.1）。</summary>
    [HttpPost("surface/abort")]
    public async Task<IActionResult> AbortSurfaceScan([FromBody] SurfaceScanAbortRequest request, CancellationToken ct)
    {
        var result = await _scan.AbortSurfaceScanAsync(request, ct);
        return DasApiActionResult.From(result);
    }

    /// <summary>下載面掃描附加檔（ZIP：STEP／glTF／PNG）。成功時直接回 ZIP 檔。</summary>
    [HttpGet("surface/{taskId}/attachments")]
    public async Task<IActionResult> GetSurfaceAttachments(string taskId, CancellationToken ct)
    {
        var result = await _scan.GetSurfaceAttachmentsAsync(taskId, ct);
        if (!result.Success || result.Data is null)
        {
            return SpecApiActionResult.From(result);
        }

        return PhysicalFile(result.Data.ZipFullPath, "application/zip", result.Data.DownloadFileName);
    }

    /// <summary>研磨區孔洞掃描，回傳孔洞邊界座標。</summary>
    [HttpPost("hole")]
    public async Task<IActionResult> ScanHole([FromBody] HoleScanRequest request, CancellationToken ct)
    {
        var result = await _scan.ScanHoleAsync(request, ct);
        return SpecApiActionResult.From(result);
    }
}
