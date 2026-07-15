using Microsoft.AspNetCore.SignalR;

namespace ThreeDScan.Api.Hubs;

public sealed class CalibrationPreviewHub : Hub
{
    public const string HubPath = "/hubs/calibration-preview";
    public const string EventName = "CalibrationPreview";
}
