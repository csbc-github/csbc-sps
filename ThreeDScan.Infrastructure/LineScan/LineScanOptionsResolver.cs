using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>合併 appsettings LineScanners 與 DB 兩點／ROI，供裝置層使用。</summary>
public sealed class LineScanOptionsResolver
{
    private readonly LineScannerRegistryOptions _registry;
    private readonly IServiceScopeFactory _scopes;

    /// <summary>建立選項解析器。</summary>
    public LineScanOptionsResolver(
        IOptions<LineScannerRegistryOptions> registry,
        IServiceScopeFactory scopes)
    {
        _registry = registry.Value;
        _scopes = scopes;
    }

    /// <summary>讀取並驗證 appsettings 端點。</summary>
    public LineScannerEndpointOptions RequireEndpoint(string scannerId)
    {
        var id = scannerId.Trim();
        if (!_registry.TryGetValue(id, out var endpoint))
        {
            throw new LineScanDeviceException("CAL_CFG_001", "未知的線掃描 scannerId。");
        }

        if (string.IsNullOrWhiteSpace(endpoint.Ip) || endpoint.Port is < 1 or > 65535
            || !double.IsFinite(endpoint.ConnectionTimeoutSeconds) || endpoint.ConnectionTimeoutSeconds <= 0
            || endpoint.ConnectionTimeoutSeconds > 300)
        {
            throw new LineScanDeviceException("CAL_CFG_001", "掃描器 IP、連接埠或連線逾時設定無效。");
        }

        return endpoint;
    }

    /// <summary>從 DB 讀取兩點桌面校正並建立 Hank <see cref="Calibration"/>。</summary>
    public async Task<Calibration> GetCalibrationAsync(string scannerId, CancellationToken ct = default)
    {
        var settings = await RequireDbSettingsAsync(scannerId, ct).ConfigureAwait(false);
        var calibration = new Calibration(
            new P(settings.CalibrationFirstX!.Value, settings.CalibrationFirstY!.Value),
            new P(settings.CalibrationSecondX!.Value, settings.CalibrationSecondY!.Value));
        try
        {
            calibration.Validate();
        }
        catch (ArgumentException error)
        {
            throw new LineScanDeviceException("CAL_CFG_001", error.Message);
        }

        return calibration;
    }

    /// <summary>合併端點與 DB ROI，建立錄製／重建選項。</summary>
    public async Task<ReconstructionOptions> GetReconstructionOptionsAsync(string scannerId, CancellationToken ct = default)
    {
        var endpoint = RequireEndpoint(scannerId);
        var settings = await RequireDbSettingsAsync(scannerId, ct).ConfigureAwait(false);
        var roi = settings.RoiBoundsOrNull();
        if (roi is not null
            && (roi.Length != 4 || roi.Any(value => !double.IsFinite(value))
                || roi[0] >= roi[1] || roi[2] >= roi[3]))
        {
            throw new LineScanDeviceException("CAL_CFG_001", "掃描器有效範圍設定無效。");
        }

        return new ReconstructionOptions
        {
            PositionSource = endpoint.PositionSource,
            WheelRadiusMm = endpoint.WheelRadiusMm,
            EncoderCountsPerRev = endpoint.EncoderCountsPerRev,
            EncoderChannel = endpoint.EncoderChannel,
            Direction = endpoint.Direction,
            AverageScans = endpoint.AverageScans,
            RoiXY = roi,
            Xmin = endpoint.Xmin,
            Xmax = endpoint.Xmax,
            MinHeight = endpoint.MinHeight,
            MaxHeight = endpoint.MaxHeight,
        };
    }

    /// <summary>線掃描開始前檢查：端點、兩點校正、Encoder 與重建參數。</summary>
    public async Task ValidateForStartAsync(string scannerId, CancellationToken ct = default)
    {
        var endpoint = RequireEndpoint(scannerId);
        if (!string.Equals(endpoint.PositionSource, "encoder", StringComparison.OrdinalIgnoreCase))
        {
            throw new LineScanDeviceException("SCAN_LINE_CONFIG", "目前原生線掃描後端要求 Encoder 定位，不會以 rollerSpeed 估算位移。");
        }

        var calibration = await GetCalibrationAsync(scannerId, ct).ConfigureAwait(false);
        var reconstruction = await GetReconstructionOptionsAsync(scannerId, ct).ConfigureAwait(false);
        try
        {
            StreamingReconstruction.Validate(calibration, reconstruction);
        }
        catch (ArgumentException error)
        {
            throw new LineScanDeviceException("SCAN_LINE_CONFIG", error.Message);
        }
    }

    /// <summary>讀取 DB 設定；缺少兩點校正時失敗。</summary>
    public async Task<Domain.Models.LineScannerSettingsRecord> RequireDbSettingsAsync(
        string scannerId,
        CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ILineScannerSettingsRepository>();
        var record = await repo.GetAsync(scannerId, ct).ConfigureAwait(false);
        if (record is null || !record.HasCalibration)
        {
            throw new LineScanDeviceException(
                "CAL_CFG_001",
                "請先設定桌面校正的 calibrationFirst 與 calibrationSecond。");
        }

        return record;
    }
}
