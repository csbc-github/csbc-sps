using Microsoft.AspNetCore.Mvc;
using ThreeDScan.Api.Common;
using ThreeDScan.Application.Calibration;
using ThreeDScan.Application.Calibration.Models;

namespace ThreeDScan.Api.Controllers;

/// <summary>儀器／基準校正。</summary>
[ApiController]
[Route("api/calibration")]
public sealed class CalibrationController : ControllerBase
{
    private readonly ICalibrationAppService _calibration;

    /// <summary>建立校正 API。</summary>
    public CalibrationController(ICalibrationAppService calibration)
    {
        _calibration = calibration;
    }

    // 舊版 POST /api/calibration/instrument（final + 單 URL 兩次）已停用；台船請用 instrument/start + instrument/complete。
    // [HttpPost("instrument")]
    // public async Task<IActionResult> CalibrateInstrument(
    //     [FromBody] InstrumentCalibrationRequest request,
    //     CancellationToken ct)
    // {
    //     var result = await _calibration.CalibrateInstrumentAsync(request, ct);
    //     return SpecApiActionResult.From(result);
    // }

    /// <summary>儀器校正第一次拍攝（指南 v1.1）。</summary>
    [HttpPost("instrument/start")]
    public async Task<IActionResult> StartInstrumentCalibration(
        [FromBody] InstrumentCalibrationStartRequest request,
        CancellationToken ct)
    {
        var result = await _calibration.StartInstrumentCalibrationAsync(request, ct);
        return DasApiActionResult.From(result);
    }

    /// <summary>儀器校正第二次拍攝（指南 v1.1）。接受後以 callback／GET 查詢結果。</summary>
    [HttpPost("instrument/complete")]
    public async Task<IActionResult> CompleteInstrumentCalibration(
        [FromBody] InstrumentCalibrationCompleteRequest request,
        CancellationToken ct)
    {
        var result = await _calibration.CompleteInstrumentCalibrationAsync(request, ct);
        return DasApiActionResult.Accepted(result);
    }

    /// <summary>
    /// 開發用 Ref 建立。成功回基準欄位，失敗回 error.code。不推送 WPF。
    /// 現場：測試掃描讀球心 → 寫入 ScannerConfig.Ref* → 呼叫本 API。
    /// 拍攝一張，在 DB 指定 XY 擬合；成功後覆寫 Ref X/Y 與 ref_*.png。
    /// </summary>
    [HttpPost("reference-setup")]
    public async Task<IActionResult> SetupReference(
        [FromBody] ReferenceSetupRequest request,
        CancellationToken ct)
    {
        var result = await _calibration.SetupReferenceAsync(request, ct);
        return SpecApiActionResult.From(result);
    }

    // 舊版 POST /api/calibration/roller 已停用；現場改由 WPF 兩點校正，不再列入 DAS 契約。
    // [HttpPost("roller")]
    // public async Task<IActionResult> CalibrateRoller(
    //     [FromBody] RollerCalibrationRequest request,
    //     CancellationToken ct)
    // {
    //     var result = await _calibration.CalibrateRollerAsync(request, ct);
    //     return SpecApiActionResult.From(result);
    // }
}
