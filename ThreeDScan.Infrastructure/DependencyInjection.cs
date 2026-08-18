using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.Data;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Measure;
using ThreeDScan.Infrastructure.Persistence;
using ThreeDScan.Infrastructure.Scanners;

namespace ThreeDScan.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddThreeDScanInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ScannerRegistryOptions>(configuration.GetSection("Scanners"));
        services.AddSingleton<IOptions<LjCalibrationParameters>>(
            _ => Options.Create(new LjCalibrationParameters()));
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.AddSingleton<IStorageSettings, StorageSettings>();
        services.Configure<PlyProcessorOptions>(configuration.GetSection(PlyProcessorOptions.SectionName));
        services.AddSingleton(sp =>
            new HankPlyProcessorClient(sp.GetRequiredService<IOptions<PlyProcessorOptions>>().Value));
        services.AddSingleton<AngleSteelPipelineExporter>();

        var connectionString = configuration.GetConnectionString("ThreeDScan")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:ThreeDScan or DefaultConnection is required.");

        services.AddDbContext<ThreeDScanDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddSingleton<IScannerRegistry, ScannerRegistry>();
        services.AddSingleton<IScannerCaptureFactory, ScannerCaptureFactory>();
        services.AddSingleton<ILjSphereMeasureEngine, LjDeveloperMeasureEngine>();
        services.AddScoped<ICompensationStore, CompensationStore>();
        services.AddScoped<IScannerConfigStore, ScannerConfigStore>();
        services.AddScoped<IScannerProgramSettingsStore, ScannerProgramSettingsStore>();
        services.AddScoped<IScannerProgramGroupStore, ScannerProgramGroupStore>();
        services.AddScoped<IScannerProgramSettingsService, ScannerProgramSettingsService>();
        services.AddScoped<IOperationLogStore, OperationLogStore>();
        services.AddScoped<IInstrumentCalibrationTaskStore, InstrumentCalibrationTaskStore>();

        return services;
    }
}
