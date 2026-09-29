using Microsoft.AspNetCore.Mvc;
using ThreeDScan.Api.Common;
using ThreeDScan.Application.Scan;

namespace ThreeDScan.Api.Controllers;

/// <summary>指南任務結果查詢。</summary>
[ApiController]
[Route("api/tasks")]
public sealed class TasksController : ControllerBase
{
    private readonly IScanAppService _scan;

    /// <summary>建立任務查詢 API。</summary>
    public TasksController(IScanAppService scan)
    {
        _scan = scan;
    }

    /// <summary>查詢任務結果（指南 v1.1）。DAS 預設每秒輪詢。</summary>
    [HttpGet("{taskId}/result")]
    public async Task<IActionResult> GetResult(string taskId, CancellationToken ct)
    {
        var result = await _scan.GetTaskResultAsync(taskId, ct);
        return DasApiActionResult.From(result);
    }
}
