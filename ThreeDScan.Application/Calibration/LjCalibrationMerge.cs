using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Calibration;

/// <summary>合併 DB ScannerConfig 與程式內建預設 LJ 校正參數（DB 優先）。</summary>
public static class LjCalibrationMerge
{
    public static LjCalibrationParameters ForScanner(
        ScannerConfigRecord? config,
        LjCalibrationParameters appDefaults)
    {
        var plane = appDefaults.DatumPlane;
        if (config is null)
            return appDefaults;

        return new LjCalibrationParameters
        {
            ExpectedBallDiameter = config.ExpectedBallDiameter ?? appDefaults.ExpectedBallDiameter,
            DiameterTolerance = config.DiameterTolerance ?? appDefaults.DiameterTolerance,
            MaxDiameterError = config.MaxDiameterError ?? appDefaults.MaxDiameterError,
            BallCount = config.BallCount ?? appDefaults.BallCount,
            DatumPlane = new DatumPlaneSettings
            {
                Enabled = config.DatumPlaneEnabled ?? plane.Enabled,
                CenterX = config.DatumPlaneCenterX ?? plane.CenterX,
                CenterY = config.DatumPlaneCenterY ?? plane.CenterY,
                Width = config.DatumPlaneWidth ?? plane.Width,
                Height = config.DatumPlaneHeight ?? plane.Height,
                Angle = config.DatumPlaneAngle ?? plane.Angle,
            },
        };
    }
}
