using Microsoft.AspNetCore.Mvc;
using ThreeDScan.Api.Common;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Api.Controllers;

/// <summary>任務產物下載。對應 Hank <c>/api/files/{taskId}/{artifactId}</c>。</summary>
[ApiController]
[Route("api/files")]
public sealed class ScanFilesController : ControllerBase
{
    private readonly IScanOrchestrator _scans;

    /// <summary>建立下載控制器。</summary>
    public ScanFilesController(IScanOrchestrator scans)
    {
        _scans = scans;
    }

    /// <summary>下載任務產物；需 Bearer。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ArtifactService</c>。</remarks>
    [HttpGet("{taskId}/{artifactId}")]
    public async Task<IActionResult> Get(string taskId, string artifactId, CancellationToken ct)
    {
        try
        {
            var file = await _scans.GetArtifactAsync(taskId, artifactId, ct).ConfigureAwait(false);
            return PhysicalFile(file.Path, file.ContentType, file.DownloadName);
        }
        catch (ScanTaskException ex)
        {
            return DasApiActionResult.From(DasOperationResult<object>.Fail(ex.Code, ex.Message));
        }
    }
}
