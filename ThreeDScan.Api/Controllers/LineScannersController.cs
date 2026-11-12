using Microsoft.AspNetCore.Mvc;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;

namespace ThreeDScan.Api.Controllers;

/// <summary>線掃描器設定（Hank 擴充 API）。</summary>
[ApiController]
[Route("api/scanners")]
public sealed class LineScannersController : ControllerBase
{
    private readonly LineScannerSettingsAppService _settings;

    /// <summary>建立控制器。</summary>
    public LineScannersController(LineScannerSettingsAppService settings)
    {
        _settings = settings;
    }

    /// <summary>讀取兩點與 ROI。</summary>
    [HttpGet("{scannerId}/settings")]
    public async Task<IActionResult> GetSettings(string scannerId, CancellationToken ct)
    {
        var traceId = Guid.NewGuid().ToString("N");
        var dto = await _settings.GetAsync(scannerId, traceId, ct).ConfigureAwait(false);
        if (dto is null)
        {
            return BadRequest(ToError("CAL_CFG_001", "未知 scannerId。"));
        }

        return Ok(dto);
    }

    /// <summary>儲存兩點桌面校正。</summary>
    [HttpPost("{scannerId}/calibration/two-point")]
    public async Task<IActionResult> SetTwoPoint(
        string scannerId,
        [FromBody] LineScannerTwoPointRequest request,
        CancellationToken ct)
    {
        var traceId = Guid.NewGuid().ToString("N");
        try
        {
            var dto = await _settings.SetTwoPointAsync(scannerId, request, traceId, ct).ConfigureAwait(false);
            if (dto is null)
            {
                return BadRequest(ToError("CAL_CFG_001", "未知 scannerId。"));
            }

            return Ok(dto);
        }
        catch (InvalidOperationException ex) when (ex.Message == "SCANNER_BUSY")
        {
            return BadRequest(ToError("SCANNER_BUSY", "此掃描器仍有任務進行中，請先完成或中止，再修改校正與框選範圍。"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ToError("CAL_CFG_001", ex.Message));
        }
    }

    /// <summary>清除兩點桌面校正。</summary>
    [HttpDelete("{scannerId}/calibration/two-point")]
    public async Task<IActionResult> ClearTwoPoint(string scannerId, CancellationToken ct)
    {
        var traceId = Guid.NewGuid().ToString("N");
        try
        {
            var dto = await _settings.ClearTwoPointAsync(scannerId, traceId, ct).ConfigureAwait(false);
            if (dto is null)
            {
                return BadRequest(ToError("CAL_CFG_001", "未知 scannerId。"));
            }

            return Ok(dto);
        }
        catch (InvalidOperationException ex) when (ex.Message == "SCANNER_BUSY")
        {
            return BadRequest(ToError("SCANNER_BUSY", "此掃描器仍有任務進行中，請先完成或中止，再修改校正與框選範圍。"));
        }
    }

    /// <summary>儲存或清除 ROI。</summary>
    [HttpPost("{scannerId}/roi")]
    public async Task<IActionResult> SetRoi(
        string scannerId,
        [FromBody] LineScannerRoiRequest request,
        CancellationToken ct)
    {
        var traceId = Guid.NewGuid().ToString("N");
        try
        {
            var dto = await _settings.SetRoiAsync(scannerId, request, traceId, ct).ConfigureAwait(false);
            if (dto is null)
            {
                return BadRequest(ToError("CAL_CFG_001", "未知 scannerId。"));
            }

            return Ok(dto);
        }
        catch (InvalidOperationException ex) when (ex.Message == "SCANNER_BUSY")
        {
            return BadRequest(ToError("SCANNER_BUSY", "此掃描器仍有任務進行中，請先完成或中止，再修改校正與框選範圍。"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ToError("CAL_CFG_001", ex.Message));
        }
    }

    private static DasErrorBody ToError(string code, string message)
    {
        return new DasErrorBody
        {
            Error = new TaskErrorDto
            {
                Code = code,
                Message = message,
            },
        };
    }
}
