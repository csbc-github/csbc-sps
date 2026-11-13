using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Scan;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.LineScan;
using ThreeDScan.Infrastructure.Measure;
using ThreeDScan.Infrastructure.Repositories;
using ThreeDScan.Infrastructure.Scan;
using ThreeDScan.Infrastructure.Scanners;

namespace ThreeDScan.Infrastructure;

/// <summary>註冊資料庫、掃描器、量測與匯出服務。</summary>
public static class DependencyInjection
{
    /// <summary>加入 Infrastructure 相依性。</summary>
    public static IServiceCollection AddThreeDScanInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ScannerRegistryOptions>(configuration.GetSection("Scanners"));
        services.Configure<LineScannerRegistryOptions>(configuration.GetSection("LineScanners"));
        services.Configure<LineScanEncoderSimulationOptions>(
            configuration.GetSection(LineScanEncoderSimulationOptions.SectionName));
        services.Configure<SurfaceScanOrchestratorOptions>(
            configuration.GetSection(SurfaceScanOrchestratorOptions.SectionName));
        services.AddSingleton<IOptions<LjCalibrationParameters>>(
            _ => Options.Create(new LjCalibrationParameters()));
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.AddSingleton<IStorageSettings, StorageSettings>();
        services.Configure<PlyProcessorOptions>(configuration.GetSection(PlyProcessorOptions.SectionName));
        services.AddSingleton<ISurfaceScanPipeline, RawPlyScanPipeline>();
        services.AddSingleton<ISurfaceScanTaskService, SurfaceScanTaskService>();
        services.AddSingleton<ILineScannerRegistry, LineScannerRegistry>();
        // v1 legacy：ExportRoot/line-tasks；v2 線掃描任務改 IScanTaskRepository（tasks/{id}/）
        // services.AddSingleton<ILineScanTaskService, LineScanTaskService>();
        // v1 legacy：ExportRoot/line-roi.json；v2 ROI 改 LineScannerSettingsRepository
        // services.AddSingleton<ILineScanRegionStore, LineScanRegionStore>();
        services.AddSingleton<LineScanOptionsResolver>();
        services.AddSingleton<LineScanEncoderSimulationPolicy>();
        services.AddSingleton<LineScanDeviceHost>();
        services.AddSingleton<ILineScanDeviceFacade, LineScanDeviceFacade>();
        services.AddSingleton<LineScanMeasureService>();
        services.AddScoped<ILineScannerSettingsRepository, LineScannerSettingsRepository>();
        // services.AddSingleton<ISickLineScanner, SickPythonLineScanner>();
        services.AddSingleton<ISickLineScanner, SickCSharpLineScanner>();
        // v1 legacy：session/frames.jsonl；v2 量測改 LineScanMeasureService（line_raw_3d.ply）
        // services.AddSingleton<ICSharpLineMeasure, CSharpLineMeasureService>();

        var connectionString = configuration.GetConnectionString("ThreeDScan")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:ThreeDScan or DefaultConnection is required.");

        services.AddDbContext<ThreeDScanDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddSingleton<SystemRuntimeConfig>();
        services.AddSingleton<ISystemRuntimeConfig>(sp => sp.GetRequiredService<SystemRuntimeConfig>());
        services.AddSingleton<IScannerRegistry, ScannerRegistry>();
        services.AddSingleton<IScannerCaptureFactory, ScannerCaptureFactory>();
        services.AddSingleton<ILjSphereMeasureService, LjDeveloperMeasureService>();
        services.AddScoped<ICompensationRepository, CompensationRepository>();
        services.AddScoped<IScannerConfigRepository, ScannerConfigRepository>();
        services.AddScoped<IScannerProgramSettingsRepository, ScannerProgramSettingsRepository>();
        services.AddScoped<IScannerProgramGroupRepository, ScannerProgramGroupRepository>();
        services.AddScoped<IScannerProgramSettingsService, ScannerProgramSettingsService>();
        services.AddScoped<IOperationLogRepository, OperationLogRepository>();
        services.AddScoped<IInstrumentCalibrationTaskRepository, InstrumentCalibrationTaskRepository>();
        services.AddScoped<IJobThicknessRepository, JobThicknessRepository>();
        services.AddScoped<IScanCallLogRepository, ScanCallLogRepository>();
        services.AddScoped<IRollerCalibrationRepository, RollerCalibrationRepository>();
        services.AddSingleton<ScanTaskGate>();
        services.AddSingleton<IDeviceCoordinator, DeviceCoordinator>();
        services.AddScoped<IScanTaskRepository, ScanTaskRepository>();
        services.AddScoped<ISystemConfigRepository, SystemConfigRepository>();
        services.AddScoped<IScanOrchestrator, ScanOrchestrator>();
        services.AddSingleton<IHoleBoundaryReader, HoleBoundaryReader>();

        return services;
    }
}
