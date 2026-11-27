using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>將 OneClick 的局部掃描格點接回既有 scanner 外參，不改動原始 PLY。</summary>
public static class HoleCaptureTransforms
{
    /// <summary>請求 ROI 維持 scanner XYZ 定義；換算為原始局部格網後才進行選點。</summary>
    public static ScannerRoi? ToCaptureRoi(ScannerRoi? scannerRoi, SegmentCaptureMetadata? capture)
    {
        if (scannerRoi is null || capture is null
            || !string.Equals(capture.CoordinateOrigin, "local-grid", StringComparison.OrdinalIgnoreCase))
            return scannerRoi;
        if (!double.IsFinite(capture.ScannerXStartMm) || !double.IsFinite(capture.ScannerYStartMm))
            throw new InvalidDataException("Local-grid capture has an invalid SDK origin.");
        return new ScannerRoi
        {
            MinX = scannerRoi.MinX - capture.ScannerXStartMm,
            MaxX = scannerRoi.MaxX - capture.ScannerXStartMm,
            MinY = scannerRoi.MinY - capture.ScannerYStartMm,
            MaxY = scannerRoi.MaxY - capture.ScannerYStartMm,
            MinZ = scannerRoi.MinZ,
            MaxZ = scannerRoi.MaxZ,
        };
    }

    public static double[][] FromPose(Pose5 pose, CoordinateTransformOptions coordinates,
        SegmentCaptureMetadata? capture)
    {
        var scannerToAbsolute = RigidTransforms.FromPose(pose, coordinates);
        if (capture is null || !string.Equals(capture.CoordinateOrigin, "local-grid", StringComparison.OrdinalIgnoreCase))
            return scannerToAbsolute;

        if (!double.IsFinite(capture.ScannerXStartMm) || !double.IsFinite(capture.ScannerYStartMm))
            throw new InvalidDataException("Local-grid capture has an invalid SDK origin.");
        var gridToScanner = RigidTransforms.Identity();
        gridToScanner[0][3] = capture.ScannerXStartMm;
        gridToScanner[1][3] = capture.ScannerYStartMm;
        return RigidTransforms.Multiply(scannerToAbsolute, gridToScanner);
    }
}
