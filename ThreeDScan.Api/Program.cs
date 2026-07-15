using Microsoft.Extensions.FileProviders;
using ThreeDScan.Api.Hubs;
using ThreeDScan.Api.Services;
using ThreeDScan.Application.Calibration;
using ThreeDScan.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials());
});

builder.Services.Configure<CalibrationSettings>(builder.Configuration.GetSection("Calibration"));
builder.Services.AddThreeDScanInfrastructure(builder.Configuration);
builder.Services.AddSingleton<InstrumentCalibrationService>();
builder.Services.AddScoped<ICalibrationAppService, CalibrationAppService>();
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseHttpsRedirection();
app.MapControllers();
app.MapHub<CalibrationPreviewHub>(CalibrationPreviewHub.HubPath);

app.Run();
