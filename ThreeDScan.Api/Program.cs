using Microsoft.Extensions.FileProviders;
using ThreeDScan.Api.Auth;
using ThreeDScan.Api.Hubs;
using ThreeDScan.Api.Services;
using ThreeDScan.Application.Calibration;
using ThreeDScan.Application.Scan;
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
builder.Services.AddThreeDScanInfrastructure(builder.Configuration);
builder.Services.AddSingleton<InstrumentCalibrationService>();
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

app.UseCors();
app.UseHttpsRedirection();
// IdP：啟用時掛 Authentication／Authorization 中介軟體
app.UseThreeDScanAuthentication();
// IdP：對應 Controllers／Hub；Enabled=true 時 RequireAuthorization
app.MapThreeDScanAuthenticatedEndpoints();

app.Run();
