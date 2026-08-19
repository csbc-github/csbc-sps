using Microsoft.AspNetCore.Mvc;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Dtos;

namespace ThreeDScan.Api.Controllers;

/// <summary>線掃描、面掃描與孔洞掃描。</summary>
[ApiController]
[Route("api/scan")]
public sealed class ScanController : ControllerBase
{
    private readonly IScanAppService _scan;

    public ScanController(IScanAppService scan) => _scan = scan;

    /// <summary>預備進料區線掃描：滾輪傳動時持續辨識角鋼輪廓。</summary>
    [HttpPost("line")]
    public async Task<IActionResult> ScanLine([FromBody] LineScanRequest request, CancellationToken ct)
    {
        var result = await _scan.ScanLineAsync(request, ct);
        return SpecApiActionResult.From(result);
    }

    /// <summary>噴印區／割切區面掃描；可分段呼叫，最後一次完成拼接。</summary>
    [HttpPost("surface")]
    public async Task<IActionResult> ScanSurface([FromBody] SurfaceScanRequest request, CancellationToken ct)
    {
        var result = await _scan.ScanSurfaceAsync(request, ct);
        return SpecApiActionResult.From(result);
    }

    /// <summary>下載面掃描附加檔（ZIP：STEP／glTF／PNG）。</summary>
    [HttpGet("surface/{taskId}/attachments")]
    public async Task<IActionResult> GetSurfaceAttachments(string taskId, CancellationToken ct)
    {
        var result = await _scan.GetSurfaceAttachmentsAsync(taskId, ct);
        return SpecApiActionResult.From(result);
    }

    /// <summary>研磨區孔洞掃描，回傳孔洞邊界座標。</summary>
    [HttpPost("hole")]
    public async Task<IActionResult> ScanHole([FromBody] HoleScanRequest request, CancellationToken ct)
    {
        var result = await _scan.ScanHoleAsync(request, ct);
        return SpecApiActionResult.From(result);
    }
}
