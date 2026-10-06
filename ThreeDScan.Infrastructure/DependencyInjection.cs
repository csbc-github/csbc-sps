using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.LineScan;
using ThreeDScan.Infrastructure.Measure;
using ThreeDScan.Infrastructure.Repositories;
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
        services.AddSingleton<IOptions<LjCalibrationParameters>>(
            _ => Options.Create(new LjCalibrationParameters()));
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.AddSingleton<IStorageSettings, StorageSettings>();
        services.Configure<PlyProcessorOptions>(configuration.GetSection(PlyProcessorOptions.SectionName));
        services.AddSingleton<ISurfaceScanPipeline, RawPlyScanPipeline>();
        services.AddSingleton<ISurfaceScanTaskService, SurfaceScanTaskService>();
        services.AddSingleton<ILineScannerRegistry, LineScannerRegistry>();
        services.AddSingleton<ILineScanTaskService, LineScanTaskService>();
        services.AddSingleton<ILineScanRegionStore, LineScanRegionStore>();
        services.AddSingleton<ISickLineScanner, SickPythonLineScanner>();
        services.AddSingleton<ICSharpLineMeasure, CSharpLineMeasureService>();

        var connectionString = configuration.GetConnectionString("ThreeDScan")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:ThreeDScan or DefaultConnection is required.");

        services.AddDbContext<ThreeDScanDbContext>(options =>
            options.UseSqlServer(connectionString));

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

        return services;
    }
}
