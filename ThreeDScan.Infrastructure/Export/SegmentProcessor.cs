using System.Text.Json;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>單段抽取、兩翼擬合與 PLY 輸出。</summary>
/// <remarks>高點建立候選、連續實測板面恢復低邊，再以獨立平面辨識兩翼。</remarks>
public sealed class SegmentProcessor
{
    private readonly CoordinateTransformOptions _coordinates;

    /// <summary>建立處理器。</summary>
    /// <param name="options">座標選項；null 用預設。</param>
    public SegmentProcessor(CoordinateTransformOptions? options = null)
    {
        _coordinates = options ?? new CoordinateTransformOptions();
    }

    /// <summary>處理單段原始 PLY。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.SegmentProcessor</c>。</remarks>
    /// <param name="rawPly">原始 PLY。</param>
    /// <param name="outputDirectory">輸出資料夾。</param>
    /// <param name="pose">相機位姿。</param>
    /// <param name="innerLeft">左內側點。</param>
    /// <param name="innerRight">右內側點。</param>
    /// <param name="cancellationToken">取消。</param>
    /// <param name="processingOptions">抽取選項。</param>
    public SegmentProcessingResult ProcessSegment(
        string rawPly,
        string outputDirectory,
        Pose5 pose,
        Point3? innerLeft = null,
        Point3? innerRight = null,
        CancellationToken cancellationToken = default,
        SegmentProcessingOptions? processingOptions = null)
    {
        var options = processingOptions ?? new SegmentProcessingOptions();
        if (!double.IsFinite(options.MaximumPlaneRmsMm) || options.MaximumPlaneRmsMm <= 0
            || !double.IsFinite(options.ThicknessLocalRadiusMm) || options.ThicknessLocalRadiusMm <= 0)
        {
            throw new ArgumentException("平面品質門檻與厚度局部半徑須為正數。");
        }

        ValidateRoi(options.ScannerRoi);
        var matrix = RigidTransforms.FromPose(pose, _coordinates);
        rawPly = Path.GetFullPath(rawPly);
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        string scanner = Path.Combine(outputDirectory, "angle_steel_scanner.ply");
        string absolute = Path.Combine(outputDirectory, "angle_steel_absolute.ply");
        string report = Path.Combine(outputDirectory, "segment-processing.json");
        if (new[] { scanner, absolute, report }.Any(File.Exists))
        {
            throw new IOException("分段結果已存在，請使用新的輸出資料夾。");
        }

        var cloud = ScanPly.Read(rawPly);
        var extractionOptions = new ExtractionOptions(options.ZThresholdMm, options.CellMm, options.MinimumComponentCells)
        {
            RecoverMeasuredEdges = options.RecoverMeasuredEdges,
        };
        var extraction = AngleSteelExtractor.Process(
            cloud, extractionOptions,
            cancellationToken: cancellationToken);
        var candidates = new List<ComponentCandidate>();
        var candidatePoints = new Dictionary<int, (ScanVector[] Points, int[] Indices, WingFit? Fit)>();
        var localSupportDiagnostics = new Dictionary<int, LocalSupportDiagnostics>();
        var localSupportWarnings = new Dictionary<int, string>();
        int nextCandidateRank = extraction.Components.Select(c => c.Rank).DefaultIfEmpty(0).Max() + 1;
        foreach (var component in extraction.Objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var indices = component.AbovePointIndices.Select(i => extraction.AbovePoints[i].SourcePointIndex)
                .Where(i => Inside(cloud.Points[i].Position, options.ScannerRoi)).ToArray();
            if (indices.Length == 0)
            {
                continue;
            }

            var points = indices.Select(i => cloud.Points[i].Position).ToArray();
            WingFit? fit = null;
            string? reason = null;
            bool planar = false;
            var planeCandidates = points.Length >= 500
                ? WingPlaneSegmentation.FindPlanes(points, Math.Clamp(options.MaximumPlaneRmsMm * 1.5, .25, 1), 4, cancellationToken)
                : [];
            GeometryFitting.TryFitWings(points, indices, options.MaximumPlaneRmsMm,
                out fit, out reason, cancellationToken, planeCandidates);

            try
            {
                var plane = GeometryFitting.Plane(points);
                planar = plane.Rms <= options.MaximumPlaneRmsMm && plane.Indices.Length >= points.Length * .65;
                // A flat-scanning fixture also has feet/vertical walls. Identify its broad upper face,
                // while exporting the entire original component for organized-row hole analysis.
                if (!planar && fit is null)
                {
                    double upper = GeometryFitting.Quantile(points.Select(p => p.Z), .6);
                    int[] top = Enumerable.Range(0, points.Length).Where(i => points[i].Z >= upper).ToArray();
                    var topPlane = GeometryFitting.Plane(points, top);
                    planar = topPlane.Rms <= options.MaximumPlaneRmsMm && topPlane.Indices.Length >= top.Length * .65
                        && Math.Abs(topPlane.Normal.Z) > .7;
                }
            }
            catch (InvalidOperationException)
            {
            }

            double confidence = fit is null ? 0 : Math.Clamp(1 - Math.Max(fit.Left.Rms, fit.Right.Rms) / (options.MaximumPlaneRmsMm * 2), 0, 1)
                * Math.Min(1, (fit.Left.Indices.Length + fit.Right.Indices.Length) / (double)points.Length);
            candidates.Add(new ComponentCandidate
            {
                Rank = component.Rank,
                PointCount = points.Length,
                CellCount = component.CellCount,
                ScannerBounds = ScanBounds.Of(points),
                IsTwoWing = fit is not null,
                Confidence = confidence,
                LeftPlaneRmsMm = fit?.Left.Rms,
                RightPlaneRmsMm = fit?.Right.Rms,
                WingNormalAngleDegrees = fit?.NormalAngle,
                IsPlanar = planar,
                Reason = reason,
            });
            candidatePoints[component.Rank] = (points, indices, fit);

            // A raised table can join all its workpieces into a single XY
            // component. Keep that parent for planar analysis and explicit rank
            // selection; add only independently validated two-wing children.
            if (options.Selection == ComponentSelection.AngleSteel && fit is null && planar && options.ZThresholdMm > 0)
            {
                var separated = LocalSupportPlaneSeparation.Find(
                    cloud, points, indices, extraction.Calibration, extractionOptions,
                    options.MaximumPlaneRmsMm, cancellationToken, planeCandidates);
                localSupportDiagnostics[component.Rank] = separated.Diagnostics;
                foreach (var child in separated.Children)
                {
                    int childRank = nextCandidateRank++;
                    var childFit = child.Fit;
                    double childConfidence = Math.Clamp(1 - Math.Max(childFit.Left.Rms, childFit.Right.Rms)
                        / (options.MaximumPlaneRmsMm * 2), 0, 1)
                        * Math.Min(1, (childFit.Left.Indices.Length + childFit.Right.Indices.Length)
                            / (double)child.Points.Length);
                    candidates.Add(new ComponentCandidate
                    {
                        Rank = childRank,
                        ParentComponentRank = component.Rank,
                        PointCount = child.Points.Length,
                        CellCount = child.CellCount,
                        ScannerBounds = ScanBounds.Of(child.Points),
                        IsTwoWing = true,
                        Confidence = childConfidence,
                        LeftPlaneRmsMm = childFit.Left.Rms,
                        RightPlaneRmsMm = childFit.Right.Rms,
                        WingNormalAngleDegrees = childFit.NormalAngle,
                        IsPlanar = false,
                    });
                    candidatePoints[childRank] = (child.Points, child.RawIndices, childFit);
                    localSupportWarnings[childRank] = $"從 rank {component.Rank} 的局部支承板上方分離角鋼為 rank {childRank}；"
                        + $"保留 {child.Points.Length:N0} 個原始實測點，通過完整兩翼檢查；支承板只用於選點，未改動座標。";
                }
            }
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("高度/連通格/ROI 過濾後沒有可處理物件。");
        }

        ComponentCandidate selected;
        if (options.ComponentRank is int rank)
        {
            selected = candidates.SingleOrDefault(c => c.Rank == rank) ?? throw new ArgumentException($"找不到指定群組 rank={rank}。");
        }
        else if (options.Selection == ComponentSelection.AngleSteel)
        {
            selected = candidates.OrderByDescending(c => c.IsTwoWing).ThenByDescending(c => c.Confidence)
                .ThenByDescending(c => c.PointCount).First();
        }
        else if (options.Selection == ComponentSelection.Planar)
        {
            selected = candidates.OrderByDescending(c => c.IsPlanar).ThenByDescending(c => c.PointCount).First();
        }
        else
        {
            selected = candidates.MaxBy(c => c.PointCount)!;
        }

        var chosen = candidatePoints[selected.Rank];
        var warnings = new List<string>(extraction.Warnings);
        if (localSupportWarnings.TryGetValue(selected.Rank, out string? supportWarning))
        {
            warnings.Add(supportWarning);
        }
        var scanBounds = extraction.Calibration.AlignedBounds;
        var objectBounds = selected.ScannerBounds;
        var clipped = new List<string>();
        const double boundaryTolerance = .25;
        if (objectBounds.MinX <= scanBounds.MinX + boundaryTolerance) clipped.Add("X 最小");
        if (objectBounds.MaxX >= scanBounds.MaxX - boundaryTolerance) clipped.Add("X 最大");
        if (objectBounds.MinY <= scanBounds.MinY + boundaryTolerance) clipped.Add("Y 最小");
        if (objectBounds.MaxY >= scanBounds.MaxY - boundaryTolerance) clipped.Add("Y 最大");
        if (clipped.Count > 0)
            warnings.Add($"選中物件接觸掃描範圍邊界（{string.Join("、", clipped)}）；範圍外沒有量測點，不能視為完整實體外緣，請核對擷取範圍。");
        warnings.Add("地面平面只用於物件分離與外緣恢復；輸出與平面厚度均從原始 scanner 歐氏座標計算。");
        if (!_coordinates.IsCalibrated)
        {
            warnings.Add("Scanner→mount 外參/原點尚未完成現場儀器校正；目前絕對座標依設定值計算。預設 A 繞 X、C 繞 Z，角度為度，Rz(C)*Rx(A)。");
        }

        if (candidates.Count > 1)
        {
            warnings.Add($"本次有 {candidates.Count} 個候選物件，依 {options.Selection} 選用 rank {selected.Rank}；可指定 ComponentRank 或 scanner ROI。");
        }

        SurfaceGeometry? geometry = null;
        string? geometryError = selected.Reason;
        if (chosen.Fit is null && localSupportDiagnostics.TryGetValue(selected.Rank, out var supportDiagnostic)
            && supportDiagnostic.Detected && supportDiagnostic.ValidChildCount == 0)
        {
            var failedChild = supportDiagnostic.Children.OrderByDescending(c => c.FinalPointCount).FirstOrDefault();
            geometryError = failedChild is null ? supportDiagnostic.Reason
                : $"已辨識支承板；其上方 {failedChild.FinalPointCount:N0} 個實測點未通過兩翼檢查：{failedChild.Reason}";
        }
        if (chosen.Fit is not null)
        {
            try
            {
                geometry = GeometryFitting.Geometry(chosen.Fit, matrix, innerLeft, innerRight, _coordinates, options);
            }
            catch (InvalidOperationException ex)
            {
                geometryError = ex.Message;
            }
        }

        if (geometry is null)
        {
            warnings.Add("此選取物件無法可靠擬合兩翼頭端幾何；點雲可供孔洞分析：" + geometryError);
        }
        else
        {
            if (geometry.WorkpieceFrame.Pose5ResidualDegrees > .2)
            {
                warnings.Add($"workpieceFrame 的 A/C 近似殘差為 {geometry.WorkpieceFrame.Pose5ResidualDegrees:F3}°；完整 XYZ 基底才是精確姿態。");
            }

            if (innerLeft is not null && geometry.LeftThickness is null)
            {
                warnings.Add("左厚度不可算：" + geometry.LeftThicknessDiagnostic.Reason);
            }

            if (innerRight is not null && geometry.RightThickness is null)
            {
                warnings.Add("右厚度不可算：" + geometry.RightThicknessDiagnostic.Reason);
            }

            if (geometry.LeftThickness is double lt && geometry.RightThickness is double rt
                && Math.Abs(lt - rt) > Math.Max(1, (lt + rt) * .075))
            {
                warnings.Add("左右板厚差超過 max(1 mm, 平均板厚×15%)；保留各側獨立結果，不提供合併板厚。");
            }
        }

        var properties = VertexProperties(cloud.Header);
        if (properties.Any(line => line.EndsWith(" raw_point_index", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("輸入已帶 raw_point_index，請使用原始掃描 PLY。");
        }

        string[] header = ["ply", "format ascii 1.0", "element vertex 0", .. properties, "property int raw_point_index", "end_header"];
        string Extras(int i)
        {
            return (cloud.Points[i].ExtraColumns.Length > 0 ? cloud.Points[i].ExtraColumns + " " : "") + i;
        }

        cancellationToken.ThrowIfCancellationRequested();
        ScanPly.Write(
            scanner,
            header,
            chosen.Indices.Select(i => (cloud.Points[i].Position, Extras(i))),
            chosen.Indices.Length,
            "Original scanner Euclidean frame; raw_point_index maps to source PLY; millimetres",
            true);
        ScanPly.Write(
            absolute,
            header,
            chosen.Indices.Select(i => (RigidTransforms.Apply(matrix, cloud.Points[i].Position), Extras(i))),
            chosen.Indices.Length,
            "Absolute frame by configured rigid scanner extrinsic and camera pose; millimetres",
            true);
        var result = new SegmentProcessingResult
        {
            RawPlyPath = rawPly,
            ScannerPlyPath = scanner,
            AbsolutePlyPath = absolute,
            ProcessingReportPath = report,
            SourceSha256 = cloud.SourceSha256!,
            CameraPosition = pose,
            ScannerToAbsoluteMatrix = matrix,
            AbsoluteCoordinatesCalibrated = _coordinates.IsCalibrated,
            CalibrationId = _coordinates.CalibrationId,
            SourcePointIndices = chosen.Indices,
            Candidates = candidates.ToArray(),
            SelectedComponentRank = selected.Rank,
            Warnings = warnings.ToArray(),
            Geometry = geometry,
            GeometryError = geometry is null ? geometryError : null,
            Fit = chosen.Fit,
        };
        using (var file = new FileStream(report, FileMode.CreateNew))
        {
            JsonSerializer.Serialize(file, new
            {
                schemaVersion = 1,
                unit = "mm",
                result,
                coordinates = _coordinates,
                processingOptions = options,
                extractionGroundCorrection = extraction.Calibration,
                localSupportSeparation = localSupportDiagnostics,
                groundCorrectionPurpose = "Segmentation only; never used as a physical scanner-to-world transform.",
                provenance = "Each output point retains its exact raw_point_index; all extra source vertex properties are retained.",
            }, ExtractionJson.Options);
        }

        return result;
    }

    /// <summary>取出 vertex 的 property 列。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.SegmentProcessor</c>。</remarks>
    /// <param name="header">PLY 標頭。</param>
    internal static string[] VertexProperties(string[] header)
    {
        bool vertex = false;
        var properties = new List<string>();
        foreach (string line in header)
        {
            if (line.StartsWith("element "))
            {
                vertex = line.StartsWith("element vertex ");
            }
            else if (vertex && line.StartsWith("property "))
            {
                properties.Add(line);
            }
        }

        return properties.ToArray();
    }

    private static bool Inside(ScanVector p, ScannerRoi? roi)
    {
        return roi is null
            || (p.X >= roi.MinX && p.X <= roi.MaxX && p.Y >= roi.MinY && p.Y <= roi.MaxY && p.Z >= roi.MinZ && p.Z <= roi.MaxZ);
    }

    private static void ValidateRoi(ScannerRoi? roi)
    {
        if (roi is not null && (new[] { roi.MinX, roi.MaxX, roi.MinY, roi.MaxY, roi.MinZ, roi.MaxZ }.Any(v => !double.IsFinite(v))
            || roi.MinX >= roi.MaxX || roi.MinY >= roi.MaxY || roi.MinZ >= roi.MaxZ))
        {
            throw new ArgumentException("scanner ROI 必須為有效有限範圍。");
        }
    }
}
