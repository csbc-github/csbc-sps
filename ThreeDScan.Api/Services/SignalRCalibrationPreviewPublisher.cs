using Microsoft.AspNetCore.SignalR;
using ThreeDScan.Api.Hubs;
using ThreeDScan.Application.Calibration;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Api.Services;

/// <summary>透過 SignalR 推送校正預覽至 WPF 監看端。</summary>
public sealed class SignalRCalibrationPreviewPublisher : ICalibrationPreviewPublisher
{
    private readonly IHubContext<CalibrationPreviewHub> _hub;

    public SignalRCalibrationPreviewPublisher(IHubContext<CalibrationPreviewHub> hub)
    {
        _hub = hub;
    }

    /// <inheritdoc />
    public Task PublishAsync(CalibrationPreview preview, CancellationToken ct = default)
    {
        return _hub.Clients.All.SendAsync(CalibrationPreviewHub.EventName, new
        {
            preview.ScannerId,
            preview.TraceId,
            preview.Timestamp,
            preview.Success,
            preview.ErrorMessage,
            Balls = preview.Balls.Select(b => new
            {
                b.Index,
                b.X,
                b.Y,
                b.Z,
                Diameter = b.Diameter,
                b.IsWithinTolerance,
            }),
            preview.CompensationOffsetX,
            preview.CompensationOffsetY,
            preview.CompensationOffsetZ,
            preview.Rmse,
            preview.PreviewGrayRelativePath,
            preview.PreviewHeightRelativePath,
            preview.ContentsFolder,
            Geometry = preview.Geometry is null ? null : new
            {
                preview.Geometry.Width,
                preview.Geometry.Height,
                preview.Geometry.OriginX,
                preview.Geometry.OriginY,
                preview.Geometry.CoefficientX,
                preview.Geometry.CoefficientY,
            },
            preview.RefPatternX,
            preview.RefPatternY,
            preview.RefSphereX,
            preview.RefSphereY,
            preview.ExpectedBallDiameter,
            preview.MaxDiameterError,
            preview.DiameterTolerance,
            preview.DetectCount,
            MatchedBalls = preview.MatchedBalls.Select(m => new
            {
                m.BallId,
                m.KnownX,
                m.KnownY,
                m.MeasuredX,
                m.MeasuredY,
                m.ResidualMm,
            }),
        }, ct);
    }
}
