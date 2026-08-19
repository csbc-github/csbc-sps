using Microsoft.AspNetCore.Mvc;
using ThreeDScan.Application.Calibration;
using ThreeDScan.Application.Calibration.Dtos;

namespace ThreeDScan.Api.Controllers;

[ApiController]
[Route("api/calibration")]
public sealed class CalibrationController : ControllerBase
{
    private readonly ICalibrationAppService _calibration;

    public CalibrationController(ICalibrationAppService calibration) => _calibration = calibration;

    /// <summary>SD UC-01 — 儀器校正（LJScanWinform 量測按鈕）。推送 WPF 監看預覽。</summary>
    [HttpPost("instrument")]
    public async Task<IActionResult> CalibrateInstrument(
        [FromBody] InstrumentCalibrationRequest request,
        CancellationToken ct)
    {
        var result = await _calibration.CalibrateInstrumentAsync(request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>開發用 Ref 建立（LJScanWinform 校正按鈕）。寫入 ScannerConfig，不推送畫面。</summary>
    [HttpPost("reference-setup")]
    public async Task<IActionResult> SetupReference(
        [FromBody] ReferenceSetupRequest request,
        CancellationToken ct)
    {
        var result = await _calibration.SetupReferenceAsync(request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>無校正物時掃描滾輪台，取得掃描器到滾輪的 Z 距離。</summary>
    [HttpPost("roller")]
    public async Task<IActionResult> CalibrateRoller(
        [FromBody] RollerCalibrationRequest request,
        CancellationToken ct)
    {
        var result = await _calibration.CalibrateRollerAsync(request, ct);
        return SpecApiActionResult.From(result);
    }
}
