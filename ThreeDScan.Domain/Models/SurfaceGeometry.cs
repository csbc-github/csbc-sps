namespace ThreeDScan.Domain.Models;

/// <summary>單段角鋼幾何。對應 Hank <c>Scan3D.Processing.SurfaceGeometry</c>。</summary>
public sealed class SurfaceGeometry
{
    /// <summary>頭端三點。</summary>
    public HeadFeatures HeadFeatures { get; init; } = new HeadFeatures();

    /// <summary>尾端三點。</summary>
    public HeadFeatures TailFeatures { get; init; } = new HeadFeatures();

    /// <summary>頭尾 Z 差（mm）。</summary>
    public double SegmentZDelta { get; init; }

    /// <summary>左翼厚度（mm）。</summary>
    public double? LeftThickness { get; init; }

    /// <summary>右翼厚度（mm）。</summary>
    public double? RightThickness { get; init; }

    /// <summary>工件座標框。</summary>
    public WorkpieceFrame WorkpieceFrame { get; init; } = new WorkpieceFrame();

    /// <summary>左翼平面 RMS（mm）。</summary>
    public double LeftPlaneRmsMm { get; init; }

    /// <summary>右翼平面 RMS（mm）。</summary>
    public double RightPlaneRmsMm { get; init; }

    /// <summary>兩翼法線夾角（度）。</summary>
    public double WingNormalAngleDegrees { get; init; }

    /// <summary>寬度（mm）。</summary>
    public double WidthMm { get; init; }

    /// <summary>高度（mm）。</summary>
    public double HeightMm { get; init; }

    /// <summary>觀測長度（mm）。</summary>
    public double ObservedLengthMm { get; init; }

    /// <summary>特徵方法。</summary>
    public string FeatureMethod { get; init; } = "";

    /// <summary>左厚度診斷。</summary>
    public ThicknessDiagnostic LeftThicknessDiagnostic { get; init; } = new ThicknessDiagnostic();

    /// <summary>右厚度診斷。</summary>
    public ThicknessDiagnostic RightThicknessDiagnostic { get; init; } = new ThicknessDiagnostic();
}
