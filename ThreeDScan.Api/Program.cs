using System.Net.Http;
using Microsoft.Extensions.FileProviders;
using ThreeDScan.Api.Auth;
using ThreeDScan.Api.Hubs;
using ThreeDScan.Api.Services;
using ThreeDScan.Application.Calibration;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials());
});

// IdP：註冊 JwtBearer（Enabled=true 時）與 DAS AuthenticationManager（交叉驗證用）
builder.Services.AddThreeDScanAuthentication(builder.Configuration);
builder.Services.Configure<CalibrationSettings>(builder.Configuration.GetSection("Calibration"));
builder.Services.Configure<CallbackSettings>(builder.Configuration.GetSection(CallbackSettings.SectionName));
builder.Services.Configure<WatchdogSettings>(builder.Configuration.GetSection(WatchdogSettings.SectionName));
builder.Services.Configure<SurfaceScanSettings>(builder.Configuration.GetSection(SurfaceScanSettings.SectionName));
builder.Services.Configure<LineScanSettings>(builder.Configuration.GetSection(LineScanSettings.SectionName));
builder.Services.AddThreeDScanInfrastructure(builder.Configuration);
builder.Services.AddHttpClient("DasCallback")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
    });
builder.Services.AddSingleton<IAcceptedWorkQueue, AcceptedWorkQueue>();
builder.Services.AddSingleton<CallbackAccessTokenSource>();
builder.Services.AddSingleton<ICallbackNotifier, CallbackNotifier>();
builder.Services.AddHostedService<AcceptedWorkHostedService>();
builder.Services.AddHostedService<TaskWatchdogHostedService>();
builder.Services.AddSingleton<InstrumentCalibrationService>();
builder.Services.AddSingleton<IDasTaskResultCache, DasTaskResultCache>();
builder.Services.AddScoped<ICalibrationAppService, CalibrationAppService>();
builder.Services.AddScoped<IScanAppService, ScanAppService>();
builder.Services.AddSingleton<ICalibrationPreviewPublisher, SignalRCalibrationPreviewPublisher>();

var app = builder.Build();

var contentsRoot = builder.Configuration["Storage:ContentsRoot"];
if (!string.IsNullOrWhiteSpace(contentsRoot))
{
    Directory.CreateDirectory(contentsRoot);
    app.UseStaticFiles(new StaticFileOptions
    {
        RequestPath = "/calibration-preview-files",
        FileProvider = new PhysicalFileProvider(contentsRoot),
    });
}

var exportRoot = app.Services.GetRequiredService<IStorageSettings>().ExportRoot;
if (!string.IsNullOrWhiteSpace(exportRoot))
{
    Directory.CreateDirectory(exportRoot);
    app.UseStaticFiles(new StaticFileOptions
    {
        RequestPath = "/scan-files",
        FileProvider = new PhysicalFileProvider(exportRoot),
        ServeUnknownFileTypes = true,
    });
}

app.UseCors();
app.UseHttpsRedirection();
// IdP：啟用時掛 Authentication／Authorization 中介軟體
app.UseThreeDScanAuthentication();
// IdP：對應 Controllers／Hub；Enabled=true 時 RequireAuthorization
app.MapThreeDScanAuthenticatedEndpoints();

app.Run();
