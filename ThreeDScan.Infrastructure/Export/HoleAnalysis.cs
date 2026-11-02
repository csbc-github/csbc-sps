using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>保留掃描器 XYZ 與掃描列；孔洞分析前不做地面整平。</summary>
/// <remarks>由 Hank Scan3D 的 HoleAnalysis 搬入，邏輯不改。</remarks>
public static class HoleAnalysis
{
    /// <summary>分析 PLY 孔洞。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Features.HoleAnalysis</c>。</remarks>
    /// <param name="path">PLY 路徑。</param>
    /// <param name="requestedType">指定類型 Circle／Slot；null 不限。</param>
    /// <param name="roi">掃描器 ROI。</param>
    /// <param name="cancellationToken">取消。</param>
    public static HoleAnalysisResult AnalyzeFile(
        string path,
        string? requestedType = null,
        FeatureRoi? roi = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(requestedType) && requestedType is not ("Circle" or "Slot"))
        {
            throw new NotSupportedException(requestedType == "EdgeNotch"
                ? "EdgeNotch is not supported: a partial observed hole cannot establish an open edge notch."
                : $"Unsupported hole type: {requestedType}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        float[] xyz = ScannerPlyReader.Read(path);
        if (roi is not null)
        {
            ValidateRoi(roi);
            var selected = new List<float>();
            for (int i = 0; i < xyz.Length; i += 3)
            {
                if (xyz[i] >= roi.Minimum.X && xyz[i] <= roi.Maximum.X
                    && xyz[i + 1] >= roi.Minimum.Y && xyz[i + 1] <= roi.Maximum.Y
                    && xyz[i + 2] >= roi.Minimum.Z && xyz[i + 2] <= roi.Maximum.Z)
                {
                    selected.Add(xyz[i]);
                    selected.Add(xyz[i + 1]);
                    selected.Add(xyz[i + 2]);
                }
            }

            xyz = selected.ToArray();
        }

        cancellationToken.ThrowIfCancellationRequested();
        // A capture may contain a floor, both angle faces and fixture faces before the drilled plate.
        // Search eight independent measured planes so a smaller flat hole fixture is not hidden by clutter.
        var detection = OrganizedHoleDetector.Detect(xyz, options: new HoleDetectionOptions(8));
        cancellationToken.ThrowIfCancellationRequested();
        double tolerance = Math.Max(.8, Math.Max(detection.ScannerPitch.XMm, detection.ScannerPitch.YMm) * 1.5);
        var features = detection.AllOpenings().Select(h =>
        {
            var (type, rmse) = h.Status == "CLOSED" ? Classify(h.PerimeterPoints, tolerance) : ("Unknown", (double?)null);
            return new FeatureOpening(
                h.Id,
                h.FaceId,
                type,
                h.Status,
                h.Status == "CLOSED",
                new FeaturePoint(h.X, h.Y, h.Z),
                h.PerimeterPoints.Select(p => new FeaturePoint(p.XMm, p.YMm, p.ZMm)).ToArray(),
                rmse,
                h.BoundaryBox.WidthMm,
                h.BoundaryBox.HeightMm,
                h.Scanlines,
                h.Reason);
        }).ToArray();
        var eligible = features.Where(f => f.Closed && (string.IsNullOrEmpty(requestedType) || f.Type == requestedType))
            .OrderByDescending(f => f.WidthMm * f.HeightMm).ThenBy(f => f.Id, StringComparer.Ordinal).ToArray();
        var chosen = eligible.FirstOrDefault();
        var warnings = new List<string>();
        if (features.Length > 1)
        {
            warnings.Add("Multiple features were measured. featureBoundary selects one complete feature; use features for every separate boundary.");
        }

        if (features.Any(f => !f.Closed))
        {
            warnings.Add("Partial openings are retained for review and do not prove a complete hole or an EdgeNotch.");
        }

        if (chosen is null)
        {
            warnings.Add(requestedType is null
                ? "No complete measured opening was found."
                : $"No complete opening confidently classified as {requestedType} was found.");
        }

        if (features.Any(f => f.Closed && f.Type == "Unknown"))
        {
            warnings.Add("Some complete openings do not meet the circle or capsule fit criteria; their type remains Unknown.");
        }

        warnings.Add("Boundary points retain native scanner coordinates and measurement accuracy; this does not certify 0.2 mm accuracy.");
        return new HoleAnalysisResult(
            chosen?.Boundary ?? [],
            chosen?.Id,
            features,
            "scanner",
            chosen is null ? "None" : "LargestCompleteMatchingFeatureByBoundingBoxArea",
            warnings.ToArray(),
            detection.PointCount,
            detection.ScannerPitch.XMm,
            detection.ScannerPitch.YMm);
    }

    private static void ValidateRoi(FeatureRoi roi)
    {
        if (!Finite(roi.Minimum) || !Finite(roi.Maximum) || roi.Minimum.X >= roi.Maximum.X
            || roi.Minimum.Y >= roi.Maximum.Y || roi.Minimum.Z >= roi.Maximum.Z)
        {
            throw new ArgumentException("ROI minimum must be finite and less than its maximum on all axes.", nameof(roi));
        }
    }

    /// <summary>三軸皆為有限值。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Features.HoleAnalysis</c>。</remarks>
    /// <param name="p">點。</param>
    internal static bool Finite(FeaturePoint p)
    {
        return double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
    }

    private static (string Type, double? Rmse) Classify(BoundaryPoint[] points, double tolerance)
    {
        if (points.Length < 12)
        {
            return ("Unknown", null);
        }

        double cu = points.Average(p => p.UMm), cv = points.Average(p => p.VMm);
        var xy = points.Select(p => (X: p.UMm - cu, Y: p.VMm - cv)).ToArray();
        double a = xy.Average(p => p.X * p.X), b = xy.Average(p => p.X * p.Y), d = xy.Average(p => p.Y * p.Y);
        double theta = .5 * Math.Atan2(2 * b, a - d), c = Math.Cos(theta), s = Math.Sin(theta);
        var aligned = xy.Select(p => (X: p.X * c + p.Y * s, Y: -p.X * s + p.Y * c)).ToArray();
        double minX = aligned.Min(p => p.X), maxX = aligned.Max(p => p.X), minY = aligned.Min(p => p.Y), maxY = aligned.Max(p => p.Y);
        double spanX = maxX - minX, spanY = maxY - minY;
        if (Math.Min(spanX, spanY) < tolerance * 4)
        {
            return ("Unknown", null);
        }

        // Geometric fits only classify; returned boundaries always use the measured points above.
        double[] circle = [(minX + maxX) / 2, (minY + maxY) / 2, (spanX + spanY) / 4];
        double CircleError(double[] p)
        {
            return aligned.Average(q => Math.Pow(Math.Sqrt(Math.Pow(q.X - p[0], 2) + Math.Pow(q.Y - p[1], 2)) - p[2], 2));
        }

        Optimize(circle, [tolerance, tolerance, tolerance], CircleError, p => p[2] > tolerance * 2);
        double circleRmse = Math.Sqrt(CircleError(circle));
        double[] capsule = [(minX + maxX) / 2, (minY + maxY) / 2, Math.Max(0, (spanX - spanY) / 2), spanY / 2, 0];
        double CapsuleError(double[] p)
        {
            double co = Math.Cos(p[4]), si = Math.Sin(p[4]);
            return aligned.Average(q =>
            {
                double u = (q.X - p[0]) * co + (q.Y - p[1]) * si, v = -(q.X - p[0]) * si + (q.Y - p[1]) * co;
                double delta = Math.Sqrt(Math.Pow(Math.Max(0, Math.Abs(u) - p[2]), 2) + v * v) - p[3];
                return delta * delta;
            });
        }

        Optimize(capsule, [tolerance, tolerance, tolerance, tolerance, .05], CapsuleError, p => p[2] >= 0 && p[3] > tolerance * 2);
        double capsuleRmse = Math.Sqrt(CapsuleError(capsule));
        double ratio = (capsule[2] + capsule[3]) / capsule[3];
        if (ratio >= 1.35 && capsuleRmse <= tolerance && capsuleRmse + tolerance * .15 < circleRmse)
        {
            return ("Slot", capsuleRmse);
        }

        if (circleRmse <= tolerance && spanX / spanY < 1.3 && spanX / spanY > 1 / 1.3)
        {
            return ("Circle", circleRmse);
        }

        return ("Unknown", Math.Min(circleRmse, capsuleRmse));
    }

    private static void Optimize(double[] p, double[] steps, Func<double[], double> error, Func<double[], bool> valid)
    {
        double best = error(p);
        for (int pass = 0; pass < 90; pass++)
        {
            bool improved = false;
            for (int j = 0; j < p.Length; j++)
            {
                double original = p[j], selected = original;
                foreach (int direction in new[] { -1, 1 })
                {
                    p[j] = original + steps[j] * direction;
                    double trial = valid(p) ? error(p) : double.PositiveInfinity;
                    if (trial < best)
                    {
                        best = trial;
                        selected = p[j];
                        improved = true;
                    }
                }

                p[j] = selected;
            }

            if (!improved)
            {
                for (int j = 0; j < steps.Length; j++)
                {
                    steps[j] *= .6;
                }
            }

            if (steps.Max() < 1e-4)
            {
                break;
            }
        }
    }
}
