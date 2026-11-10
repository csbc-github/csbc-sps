using Microsoft.AspNetCore.Mvc;
using ThreeDScan.Api.Common;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Models;

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

    /// <summary>進行中的錄製與最新截面，供進料區左側即時圖。</summary>
    [HttpGet("line/live")]
    public async Task<IActionResult> GetLiveFrame(CancellationToken ct)
    {
        return Ok(await _scan.GetLiveFrameAsync(ct).ConfigureAwait(false));
    }

    /// <summary>開始架設長連預覽（O8）。</summary>
    [HttpPost("line/setup/start")]
    public async Task<IActionResult> StartLineSetup([FromBody] LineScanSetupRequest request, CancellationToken ct)
    {
        var result = await _scan.StartLineSetupPreviewAsync(request, ct).ConfigureAwait(false);
        return DasApiActionResult.From(result);
    }

    /// <summary>結束架設預覽。</summary>
    [HttpPost("line/setup/stop")]
    public async Task<IActionResult> StopLineSetup([FromBody] LineScanSetupRequest request, CancellationToken ct)
    {
        var result = await _scan.StopLineSetupPreviewAsync(request, ct).ConfigureAwait(false);
        return DasApiActionResult.From(result);
    }

    /// <summary>凍結完整 XY 輪廓（Hank 擴充）。</summary>
    [HttpPost("profile")]
    public async Task<IActionResult> CaptureProfile([FromBody] LineScanProfileRequest request, CancellationToken ct)
    {
        var result = await _scan.CaptureLineProfileAsync(request, ct).ConfigureAwait(false);
        if (result.Error != null)
        {
            return DasApiActionResult.From(result);
        }

        var body = result.Data!;
        return Ok(new
        {
            scannerId = body.ScannerId,
            points = body.X.Zip(body.Y, (x, y) => new { x, y }),
            capturedAt = body.CapturedAt,
            units = body.Units,
            coordinateFrame = body.CoordinateFrame,
            averageScans = body.AverageScans,
            traceId = body.TraceId,
            encoders = body.Encoders.Select(e => new { position = e.Position, reserved = e.Reserved }),
        });
    }

    /// <summary>已廢止；請改用 POST /api/scanners/{scannerId}/roi。</summary>
    [HttpPut("line/region")]
    public IActionResult SetLineRegion([FromBody] LineScanRegion region)
    {
        return BadRequest(new
        {
            error = new
            {
                code = "DEPRECATED",
                message = "line/region 已廢止，請改用 POST /api/scanners/{scannerId}/roi。",
            },
        });
    }

    /// <summary>已廢止；請改用 POST /api/scanners/{scannerId}/roi（bounds 為 null 清除）。</summary>
    [HttpDelete("line/region")]
    public IActionResult ClearLineRegion()
    {
        return BadRequest(new
        {
            error = new
            {
                code = "DEPRECATED",
                message = "line/region 已廢止，請改用 POST /api/scanners/{scannerId}/roi。",
            },
        });
    }

    /// <summary>已廢止；請改用 GET /api/scanners/{scannerId}/settings。</summary>
    [HttpGet("line/region")]
    public IActionResult GetLineRegion()
    {
        return BadRequest(new
        {
            error = new
            {
                code = "DEPRECATED",
                message = "line/region 已廢止，請改用 GET /api/scanners/{scannerId}/settings。",
            },
        });
    }

    /// <summary>最近一次線掃描 complete 的結果，供進料區顯示。</summary>
    [HttpGet("line/latest")]
    public IActionResult GetLatestLineScan()
    {
        return Ok(_scan.GetLatestLineScanResult());
    }

    /// <summary>中止線掃描（指南 v1.1）。</summary>
    [HttpPost("line/abort")]
    public async Task<IActionResult> AbortLineScan([FromBody] LineScanAbortRequest request, CancellationToken ct)
    {
        var result = await _scan.AbortLineScanAsync(request, ct);
        return DasApiActionResult.From(result);
    }

    // 舊入口已退場：正式路徑請用 surface/start、surface/segment。
    // [HttpPost("surface")]
    // public async Task<IActionResult> ScanSurface([FromBody] SurfaceScanRequest request, CancellationToken ct)
    // {
    //     var result = await _scan.ScanSurfaceAsync(request, ct);
    //     return SpecApiActionResult.From(result);
    // }
    //
    // [HttpPost("surface-from-file")]
    // public async Task<IActionResult> ScanSurfaceFromFile([FromBody] SurfaceFromFileRequest request, CancellationToken ct)
    // {
    //     var result = await _scan.ScanSurfaceFromFileAsync(request, ct);
    //     return SpecApiActionResult.From(result);
    // }

    /// <summary>開始面掃描第一段（指南 v1.1）。</summary>
    [HttpPost("surface/start")]
    public async Task<IActionResult> StartSurfaceScan([FromBody] SurfaceScanStartRequest request, CancellationToken ct)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await _scan.StartSurfaceScanAsync(request, HttpContext.TraceIdentifier, ct, baseUrl);
        return DasApiActionResult.From(result);
    }

    /// <summary>面掃描後續分段（指南 v1.1）。</summary>
    [HttpPost("surface/segment")]
    public async Task<IActionResult> SegmentSurfaceScan([FromBody] SurfaceScanSegmentRequest request, CancellationToken ct)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _scan.SegmentSurfaceScanAsync(request, HttpContext.TraceIdentifier, idempotencyKey, ct);
        return DasApiActionResult.From(result);
    }

    /// <summary>結束面掃描（指南 v1.1）。即使只掃一段也須呼叫；接受後以 callback／GET 查詢結果。</summary>
    [HttpPost("surface/complete")]
    public async Task<IActionResult> CompleteSurfaceScan(
        [FromBody] SurfaceScanCompleteRequest request,
        CancellationToken ct)
    {
        var result = await _scan.CompleteSurfaceScanAsync(request, HttpContext.TraceIdentifier, ct);
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

    /// <summary>孔洞／邊緣掃描（指南 v1.1）。立即 202。</summary>
    [HttpPost("hole")]
    public async Task<IActionResult> ScanHole([FromBody] HoleScanRequest request, CancellationToken ct)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await _scan.StartHoleScanAsync(request, baseUrl, ct);
        return DasApiActionResult.Accepted(result);
    }
}
