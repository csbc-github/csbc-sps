namespace ThreeDScan.Domain.Models;

/// <summary>
/// 掃描器到絕對座標的轉換。對應 Hank <c>Scan3D.Processing.CoordinateTransformOptions</c>。
/// </summary>
public sealed class CoordinateTransformOptions
{
    /// <summary>掃描器到安裝座 4×4（mm；欄向量）。</summary>
    public double[][] ScannerToMount { get; init; } = CreateIdentity();

    /// <summary>世界原點平移（mm）。</summary>
    public Point3 WorldOriginOffset { get; init; } = new Point3();

    /// <summary>A 軸對應的座標軸。</summary>
    public AxisName AAxis { get; init; } = AxisName.X;

    /// <summary>C 軸對應的座標軸。</summary>
    public AxisName CAxis { get; init; } = AxisName.Z;

    /// <summary>A 軸符號。</summary>
    public double ASign { get; init; } = 1;

    /// <summary>C 軸符號。</summary>
    public double CSign { get; init; } = 1;

    /// <summary>true：先轉 C 再轉 A（R(A)×R(C)）。false：R(C)×R(A)。</summary>
    public bool ApplyCFirst { get; init; }

    /// <summary>是否已套用儀器校正。</summary>
    public bool IsCalibrated { get; init; }

    /// <summary>校正識別。</summary>
    public string CalibrationId { get; init; } = "unconfigured";

    /// <summary>內側點的參考框。</summary>
    public InnerReferenceFrame InnerReferenceFrame { get; init; } = InnerReferenceFrame.Absolute;

    /// <summary>單位矩陣。</summary>
    private static double[][] CreateIdentity()
    {
        return new[]
        {
            new[] { 1.0, 0.0, 0.0, 0.0 },
            new[] { 0.0, 1.0, 0.0, 0.0 },
            new[] { 0.0, 0.0, 1.0, 0.0 },
            new[] { 0.0, 0.0, 0.0, 1.0 },
        };
    }
}
