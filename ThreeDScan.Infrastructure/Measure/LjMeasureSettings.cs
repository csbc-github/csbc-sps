using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Measure;

/// <summary>Replaces LJScanWinform BallSettings — appsettings + ScannerConfig.</summary>
internal sealed class LjMeasureSettings
{
    private readonly LjCalibrationParameters _p;
    private readonly ScannerConfigRecord _config;

    /// <summary>以 appsettings 與 ScannerConfig 合併量測參數。</summary>
    public LjMeasureSettings(LjCalibrationParameters parameters, ScannerConfigRecord config)
    {
        _p = parameters;
        _config = config;
    }

    /// <summary>預期標準球直徑 (mm)。</summary>
    public double ExpectedBallDiameter
    {
        get
        {
            return _p.ExpectedBallDiameter;
        }
    }

    /// <summary>球徑容差 (mm)。</summary>
    public double DiameterTolerance
    {
        get
        {
            return _p.DiameterTolerance;
        }
    }

    /// <summary>預期球數。</summary>
    public int BallCount
    {
        get
        {
            return _p.BallCount;
        }
    }

    /// <summary>球徑允許最大誤差 (mm)。</summary>
    public double MaxDiameterError
    {
        get
        {
            return _p.MaxDiameterError;
        }
    }

    /// <summary>是否啟用基準面量測。</summary>
    public bool DatumPlaneEnabled
    {
        get
        {
            return _p.DatumPlane.Enabled;
        }
    }

    /// <summary>基準面 ROI 中心 X (mm)。</summary>
    public double DatumPlaneCenterX
    {
        get
        {
            return _p.DatumPlane.CenterX;
        }
    }

    /// <summary>基準面 ROI 中心 Y (mm)。</summary>
    public double DatumPlaneCenterY
    {
        get
        {
            return _p.DatumPlane.CenterY;
        }
    }

    /// <summary>基準面 ROI 寬度 (mm)。</summary>
    public double DatumPlaneWidth
    {
        get
        {
            return _p.DatumPlane.Width;
        }
    }

    /// <summary>基準面 ROI 高度 (mm)。</summary>
    public double DatumPlaneHeight
    {
        get
        {
            return _p.DatumPlane.Height;
        }
    }

    /// <summary>基準面 ROI 旋轉角（度）。</summary>
    public double DatumPlaneAngle
    {
        get
        {
            return _p.DatumPlane.Angle;
        }
    }

    /// <summary>Ref 圖樣 X (mm)。</summary>
    public double RefPatternX
    {
        get
        {
            return _config.RefPatternX;
        }
    }

    /// <summary>Ref 圖樣 Y (mm)。</summary>
    public double RefPatternY
    {
        get
        {
            return _config.RefPatternY;
        }
    }

    /// <summary>Ref 球心 X (mm)。</summary>
    public double RefSphereX
    {
        get
        {
            return _config.RefSphereX;
        }
    }

    /// <summary>Ref 球心 Y (mm)。</summary>
    public double RefSphereY
    {
        get
        {
            return _config.RefSphereY;
        }
    }

    /// <summary>球半徑 (mm)。</summary>
    public double BallRadiusMm
    {
        get
        {
            return ExpectedBallDiameter * 0.5;
        }
    }

    /// <summary>圖樣匹配半徑 (mm)。</summary>
    public double PatternRadius
    {
        get
        {
            return BallRadiusMm * 1.02;
        }
    }

    /// <summary>球面擬合半徑 (mm)。</summary>
    public double SphereRadius
    {
        get
        {
            return BallRadiusMm * 0.76;
        }
    }

    /// <summary>球心最小間距 (mm)。</summary>
    public double MinSphereCenterSeparationMm
    {
        get
        {
            return ExpectedBallDiameter;
        }
    }

    /// <summary>
    /// 校正尋球網格／PM 候選的最大搜尋半徑（mm）。
    /// 至少 25 mm，供手放球偏離種子；會讓寫回的 Ref 離開手動點的位置。
    /// </summary>
    public double CalibrationDiscoverMaxOffsetFromSeedMm
    {
        get
        {
            return Math.Max(25.0, ExpectedBallDiameter);
        }
    }

    /// <summary>避免全視野掃描的安全上限 (mm)。</summary>
    public double CalibrationDiscoverAbsoluteMaxMm
    {
        get
        {
            return 45.0;
        }
    }

    /// <summary>校正尋球實際使用的搜尋半徑 (mm)。</summary>
    public double CalibrationDiscoverSearchRadiusMm
    {
        get
        {
            return Math.Min(CalibrationDiscoverAbsoluteMaxMm, CalibrationDiscoverMaxOffsetFromSeedMm);
        }
    }

    /// <summary>校正尋球網格步距 (mm)。</summary>
    public double CalibrationDiscoverGridStepMm
    {
        get
        {
            return 3.0;
        }
    }

    /// <summary>量得球徑是否在預期容差內。</summary>
    public bool IsExpectedDiameter(double measured)
    {
        return Math.Abs(measured - ExpectedBallDiameter) <= DiameterTolerance;
    }

    /// <summary>量得球徑是否在可接受誤差內。</summary>
    public bool IsAcceptableSphereDiameter(double measured)
    {
        if (double.IsNaN(measured) || double.IsInfinity(measured)) return false;
        return Math.Abs(measured - ExpectedBallDiameter) <= MaxDiameterError;
    }
}
