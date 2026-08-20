using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Measure;
using ThreeDScan.Infrastructure.Repositories;
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

        var connectionString = configuration.GetConnectionString("ThreeDScan")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:ThreeDScan or DefaultConnection is required.");

        services.AddDbContext<ThreeDScanDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddSingleton<IScannerRegistry, ScannerRegistry>();
        services.AddSingleton<IScannerCaptureFactory, ScannerCaptureFactory>();
        services.AddSingleton<ILjSphereMeasureEngine, LjDeveloperMeasureService>();
        services.AddScoped<ICompensationRepository, CompensationRepository>();
        services.AddScoped<IScannerConfigRepository, ScannerConfigRepository>();
        services.AddScoped<IScannerProgramSettingsRepository, ScannerProgramSettingsRepository>();
        services.AddScoped<IScannerProgramGroupRepository, ScannerProgramGroupRepository>();
        services.AddScoped<IScannerProgramSettingsService, ScannerProgramSettingsService>();
        services.AddScoped<IOperationLogRepository, OperationLogRepository>();
        services.AddScoped<IInstrumentCalibrationTaskRepository, InstrumentCalibrationTaskRepository>();

        return services;
    }
}
