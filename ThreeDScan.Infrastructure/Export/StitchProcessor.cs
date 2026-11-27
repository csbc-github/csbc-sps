using System.Text.Json;
using System.Runtime.CompilerServices;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>多段拼接。CameraPose 保留絕對位置；NominalButtJoin 為明確的無重疊對接假設。</summary>
/// <remarks>由 Hank Scan3D 的 StitchProcessor 搬入，邏輯不改。</remarks>
public static class StitchProcessor
{
    // Scope the preview data to this exact result object, never to a mutable
    // file path or a future task. Packaging can reuse coordinates already in RAM.
    private static readonly ConditionalWeakTable<StitchProcessingResult, PreviewCoordinates> PreviewCache = new();

    internal static IEnumerable<ScanVector>? GetPreviewCoordinates(StitchProcessingResult result) =>
        PreviewCache.TryGetValue(result, out var data) ? data.Enumerate() : null;

    private sealed record PreviewCoordinates(ScanVector[][] Points, bool[][] Retained)
    {
        internal IEnumerable<ScanVector> Enumerate()
        {
            for (int scan = 0; scan < Points.Length; scan++)
                for (int point = 0; point < Points[scan].Length; point++)
                    if (Retained[scan][point]) yield return Points[scan][point];
        }
    }

    /// <summary>拼接多段處理結果。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.StitchProcessor</c>。</remarks>
    /// <param name="segments">各段結果。</param>
    /// <param name="outputDirectory">輸出資料夾。</param>
    /// <param name="thicknessMm">板厚；null 則嘗試由各段推導。</param>
    /// <param name="options">拼接選項。</param>
    /// <param name="cancellationToken">取消。</param>
    public static StitchProcessingResult Stitch(
        IReadOnlyList<SegmentProcessingResult> segments,
        string outputDirectory,
        double? thicknessMm = null,
        StitchProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new StitchProcessingOptions();
        if (segments.Count == 0)
        {
            throw new ArgumentException("至少需要一段掃描。");
        }

        if (thicknessMm.HasValue && (!double.IsFinite(thicknessMm.Value) || thicknessMm <= 0))
        {
            throw new ArgumentException("板厚須為正的有限值。");
        }

        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        string ply = Path.Combine(outputDirectory, "stitched.ply"), report = Path.Combine(outputDirectory, "stitch-processing.json");
        if (File.Exists(ply) || File.Exists(report))
        {
            throw new IOException("拼接結果已存在，請使用新的資料夾。");
        }

        var warnings = new List<string>();
        StitchQualityFailure[] validationFailures = [];
        bool passed = true;
        bool? alignmentPassed = null, boundaryPassed = null;
        bool calibrated = segments.All(s => s.AbsoluteCoordinatesCalibrated);
        var clouds = segments.Select(s => ScanPly.Read(s.ScannerPlyPath)).ToArray();
        var points = new ScanVector[segments.Count][];
        var retained = clouds.Select(c => Enumerable.Repeat(true, c.Points.Length).ToArray()).ToArray();
        double[][][] transforms = segments.Select(s => s.ScannerToAbsoluteMatrix).ToArray();
        foreach (var m in transforms)
        {
            RigidTransforms.Validate(m);
        }

        if (options.Mode == StitchMode.NominalButtJoin && segments.Count > 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string nominal = Path.Combine(outputDirectory, "nominal-stitch");
            var chain = StitchJob.RunPrepared(
                segments.Select(s => s.ScannerPlyPath).ToArray(),
                clouds,
                nominal,
                new ButtStitchOptions(options.NominalGapMm),
                options.ExportDiagnosticPointClouds);
            var anchor = RigidTransforms.Multiply(segments[0].ScannerToAbsoluteMatrix, RigidTransforms.Inverse(chain.Parts[0].Pose.Matrix));
            for (int i = 0; i < segments.Count; i++)
            {
                transforms[i] = RigidTransforms.Multiply(anchor, chain.Parts[i].Pose.Matrix);
                points[i] = clouds[i].Points.Select(p => RigidTransforms.Apply(transforms[i], p.Position)).ToArray();
                retained[i] = chain.Parts[i].Retained;
            }

            passed = chain.Passed;
            alignmentPassed = chain.Seams.All(s => s.Validation.ProfileSampleCount >= 30
                && s.Validation.CrossSectionHeightRmsMm <= chain.Options.MaximumValidationRmsMm
                && s.Validation.CrossSectionHeightP95AbsoluteMm <= chain.Options.MaximumValidationP95Mm
                && s.Validation.LeftEndWingSupportFraction >= .7 && s.Validation.RightEndWingSupportFraction >= .7
                && s.Validation.FitAndValidationIndicesDisjoint);
            boundaryPassed = chain.Seams.All(s => s.Validation.FullSection?.Passed == true
                && s.Validation.LeftEndInteriorCoverage?.Passed == true && s.Validation.RightEndInteriorCoverage?.Passed == true);
            validationFailures = DescribeValidationFailures(chain);
            calibrated = false;
            warnings.Add("已明確選用無重疊端對端拼接：各段長向距離假設為 nominalGapMm，cameraPosition 只錨定第一段；未驗證工件物理總長與後續段絕對位置。");
            if (chain.Seams.Any(s => s.Validation.UnfilteredFullSection?.Passed == false && s.Validation.FullSection?.Passed == true))
                warnings.Add("稀疏尾端分箱未用於翼板邊界判定；已依長向重複觀測核對兩段截面及各自內側翼寬，未因此刪除輸出的量測點。");
            if (!passed)
            {
                warnings.Add("至少一個接縫的留出截面/全寬檢查未通過；保留結果供診斷。");
                warnings.AddRange(validationFailures.Select(failure => failure.Message));
            }
        }
        else
        {
            for (int i = 0; i < segments.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                points[i] = clouds[i].Points.Select(p => RigidTransforms.Apply(transforms[i], p.Position)).ToArray();
            }

            warnings.Add("依每段 cameraPosition 與剛體外參保留絕對位置；未用幾何相似性猜測未知移動量，也未裁掉重疊量測點。");
        }

        if (!calibrated)
        {
            warnings.Add("輸出的絕對位置尚未經現場校正或物理長度驗證。");
        }

        string[] properties = SegmentProcessor.VertexProperties(clouds[0].Header);
        if (clouds.Any(c => !SegmentProcessor.VertexProperties(c.Header).SequenceEqual(properties)))
        {
            throw new InvalidDataException("各段 PLY 頂點屬性不同，無法完整保留合併。");
        }

        if (properties.Any(p => p.EndsWith(" source_scan") || p.EndsWith(" source_point_index")))
        {
            throw new InvalidDataException("來源已帶拼接索引，請使用各段原始 scanner PLY。");
        }

        string[] header = ["ply", "format ascii 1.0", "element vertex 0", .. properties,
            "property int source_scan", "property int source_point_index", "end_header"];
        IEnumerable<(ScanVector Position, string ExtraColumns)> OutputPoints()
        {
            for (int i = 0; i < clouds.Length; i++)
            {
                for (int j = 0; j < clouds[i].Points.Length; j++)
                {
                    if (retained[i][j])
                    {
                        yield return (points[i][j],
                            (clouds[i].Points[j].ExtraColumns.Length > 0 ? clouds[i].Points[j].ExtraColumns + " " : "") + i + " " + j);
                    }
                }
            }
        }

        int count = retained.Sum(r => r.Count(k => k));
        cancellationToken.ThrowIfCancellationRequested();
        ScanPly.Write(ply, header, OutputPoints(), count, "Configured rigid absolute coordinates, exact raw/source indices, millimetres", true);
        double? resolvedThickness = ResolveThickness(segments, thicknessMm, warnings);
        string? gltf = null, glb = null, step = null, modelReport = null;
        if (options.ExportModels)
        {
            if (resolvedThickness is null)
            {
                warnings.Add("未提供可用板厚；STEP/GLTF/GLB 實體模型未建立。單面點雲不能自行推測 6 mm。");
            }
            else
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var firstInverse = RigidTransforms.Inverse(transforms[0]);
                    var local = points.SelectMany((part, i) => part.Where((_, j) => retained[i][j]))
                        .Select(p => RigidTransforms.Apply(firstInverse, p)).ToArray();
                    var model = AngleSolidBuilder.Build(local, new SolidExportOptions(resolvedThickness.Value, options.MeshCellMm));
                    if (options.FillOuterEdges)
                    {
                        model = AngleSolidOutlineFiller.Fill(model, options.OuterEdgeRepairMinDepthMm, cancellationToken);
                    }
                    TransformModel(model, transforms[0]);
                    var topology = model.Validate();
                    if (options.Mode == StitchMode.NominalButtJoin && segments.Count > 1 && options.NominalGapMm == 0
                        && topology.ClosedSolidCount != 1)
                    {
                        throw new InvalidDataException($"零間隙首尾對接需建立單一連通實體，目前仍有 {topology.ClosedSolidCount} 個分離實體；未將分離模型當成已接合結果。");
                    }
                    gltf = Path.Combine(outputDirectory, "stitched.gltf");
                    glb = Path.Combine(outputDirectory, "stitched.glb");
                    step = Path.Combine(outputDirectory, "stitched.step");
                    modelReport = Path.Combine(outputDirectory, "model-export-report.json");
                    SolidGltfWriter.Write(gltf, glb, model);
                    SolidStepWriter.Write(step, model);
                    using var file = new FileStream(modelReport, FileMode.CreateNew);
                    JsonSerializer.Serialize(file, new
                    {
                        schemaVersion = 1,
                        topology,
                        model.Options,
                        model.Reconstructions,
                        edgeInfill = model.OutlineFill,
                        thicknessSource = thicknessMm is not null ? "Explicit caller dimension" : "Consistent independent left/right inner-reference predictions",
                        coordinateFrame = "STEP: absolute XYZ millimetres. GLTF/GLB: Y-up metres, (X,Y,Z) mm -> (0.001*X,0.001*Z,-0.001*Y).",
                        assumptions = new[]
                        {
                            "Planar wing reconstruction; hidden side walls and bend radius are inferred.",
                            !(model.OutlineFill?.FilledExteriorCells > 0)
                                ? "Only independently supported measured wing points and measured rounded-ridge neighbours define the CAD footprint; other original measurements stay in PLY and are not projected into extra solids. No empty seam/ridge is bridged."
                                : "Exterior notches deeper than the configured threshold are repaired within each body's fixed model envelope. Smaller edge variations and all enclosed holes are retained. Added border material is inferred. Separate major bodies and declared gaps remain separate; original measured PLY coordinates and seam-quality flags are unchanged.",
                            "Resolved footprint voids are preserved; enclosed sampling gaps up to four grid cells may be filled by the inherited solid builder.",
                            "A missing scan region may appear as a hole. Model reconstruction is not an independent dimensional inspection.",
                        },
                    }, ExtractionJson.Options);
                    if (model.OutlineFill?.FilledExteriorCells > 0)
                    {
                        warnings.Add($"模型僅修補深度超過 {options.OuterEdgeRepairMinDepthMm:0.###} mm 的外緣缺口；補入部分為推估幾何，原始點雲、內部孔洞與接縫品質判定保留。");
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException)
                {
                    warnings.Add("點雲已保留，實體重建不適用：" + ex.Message);
                    gltf = null;
                    glb = null;
                    step = null;
                    modelReport = null;
                }
            }
        }

        var result = new StitchProcessingResult
        {
            StitchedPlyPath = ply,
            ReportPath = report,
            GltfPath = gltf,
            GlbPath = glb,
            StepPath = step,
            ModelReportPath = modelReport,
            SegmentCount = segments.Count,
            PointCount = count,
            ValidationPassed = passed,
            AlignmentValidationPassed = alignmentPassed,
            BoundaryValidationPassed = boundaryPassed,
            ValidationFailures = validationFailures,
            AbsoluteCoordinatesCalibrated = calibrated,
            ThicknessMm = resolvedThickness,
            Warnings = warnings.ToArray(),
        };
        if (result.GlbPath is not null)
            PreviewCache.Add(result, new PreviewCoordinates(points, retained));
        using (var file = new FileStream(report, FileMode.CreateNew))
        {
            JsonSerializer.Serialize(file, new
            {
                schemaVersion = 1,
                result,
                options,
                validation = options.Mode == StitchMode.CameraPose
                    ? "Rigid matrix and provenance validation; overlap and physical length not verified"
                    : "Held-out full cross-section checks from inherited no-overlap stitcher",
                sources = segments.Select((s, i) => new
                {
                    sourceScan = i,
                    s.RawPlyPath,
                    s.ScannerPlyPath,
                    s.SourceSha256,
                    s.CameraPosition,
                    effectiveScannerToAbsoluteMatrix = transforms[i],
                    sourcePointIndices = s.SourcePointIndices,
                }),
                provenance = "raw_point_index maps to the original raw PLY; source_scan indexes sources; source_point_index maps to its extracted scanner PLY. No synthetic measurement points.",
            }, ExtractionJson.Options);
        }

        return result;
    }

    private static StitchQualityFailure[] DescribeValidationFailures(ChainStitchResult chain)
    {
        var failures = new List<StitchQualityFailure>();
        foreach (var seam in chain.Seams.Where(s => !s.Validation.Passed))
        {
            var validation = seam.Validation;
            var options = chain.Options;
            Check("crossSectionHeightRmsMm", "截面高度 RMS", validation.CrossSectionHeightRmsMm,
                options.MaximumValidationRmsMm, false, "mm");
            Check("crossSectionHeightP95AbsoluteMm", "截面高度 P95", validation.CrossSectionHeightP95AbsoluteMm,
                options.MaximumValidationP95Mm, false, "mm");
            Section("fullSection", "全截面", validation.FullSection);
            Section("leftEndInteriorCoverage", "前段端部相對內側翼寬", validation.LeftEndInteriorCoverage);
            Section("rightEndInteriorCoverage", "後段端部相對內側翼寬", validation.RightEndInteriorCoverage);
            Check("leftEndWingSupportFraction", "前段端部翼板支承比例", validation.LeftEndWingSupportFraction, .7, true, "ratio");
            Check("rightEndWingSupportFraction", "後段端部翼板支承比例", validation.RightEndWingSupportFraction, .7, true, "ratio");

            void Section(string prefix, string label, SectionEdgeCheck? section)
            {
                if (section is null) return;
                Check(prefix + ".leftEdgeDistanceMm", label + "左邊緣距離", section.LeftEdgeDistanceMm,
                    options.MaximumSectionEdgeDistanceMm, false, "mm");
                Check(prefix + ".rightEdgeDistanceMm", label + "右邊緣距離", section.RightEdgeDistanceMm,
                    options.MaximumSectionEdgeDistanceMm, false, "mm");
                Check(prefix + ".commonCoverageRatio", label + "共同覆蓋比例", section.CommonCoverageRatio,
                    options.MinimumSectionCoverageRatio, true, "ratio");
            }

            void Check(string name, string label, double actual, double limit, bool minimum, string unit)
            {
                if (double.IsFinite(actual) && (minimum ? actual >= limit : actual <= limit)) return;
                string valueText = double.IsFinite(actual)
                    ? actual.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "無有效量測";
                string limitText = limit.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                failures.Add(new StitchQualityFailure
                {
                    SeamNumber = seam.SeamNumber,
                    Check = name,
                    ActualValue = double.IsFinite(actual) ? actual : null,
                    Limit = limit,
                    Comparison = minimum ? ">=" : "<=",
                    Unit = unit,
                    Message = $"接縫 {seam.SeamNumber} {label} {valueText} {unit}，須{(minimum ? "至少" : "不超過")} {limitText} {unit}。",
                });
            }
        }

        return failures.ToArray();
    }

    private static double? ResolveThickness(IReadOnlyList<SegmentProcessingResult> segments, double? supplied, List<string> warnings)
    {
        if (supplied is not null)
        {
            return supplied;
        }

        var pairs = segments.Select(s => s.Geometry).Where(g => g?.LeftThickness is not null && g.RightThickness is not null).ToArray();
        var values = new List<double>();
        foreach (var g in pairs)
        {
            double left = g!.LeftThickness!.Value, right = g.RightThickness!.Value;
            if (Math.Abs(left - right) > Math.Max(1, (left + right) * .075))
            {
                continue;
            }

            values.Add((left + right) / 2);
        }

        if (values.Count == 0)
        {
            return null;
        }

        double mean = values.Average();
        if (values.Max() - values.Min() > Math.Max(1, mean * .15))
        {
            warnings.Add("跨段板厚不一致，必須明確提供模型設計板厚。");
            return null;
        }

        return mean;
    }

    private static void TransformModel(SolidModel model, double[][] matrix)
    {
        for (int i = 0; i < model.Vertices.Count; i++)
        {
            model.Vertices[i] = RigidTransforms.Apply(matrix, model.Vertices[i]);
        }

        for (int i = 0; i < model.Planes.Count; i++)
        {
            model.Planes[i] = new SolidPlane(RigidTransforms.Apply(matrix, model.Planes[i].Origin), RigidTransforms.Direction(matrix, model.Planes[i].Normal));
        }

        for (int i = 0; i < model.Reconstructions.Count; i++)
        {
            var r = model.Reconstructions[i];
            model.Reconstructions[i] = new SolidReconstruction(
                RigidTransforms.Apply(matrix, r.Origin),
                RigidTransforms.Direction(matrix, r.CrossAxis),
                RigidTransforms.Direction(matrix, r.LongAxis),
                RigidTransforms.Direction(matrix, r.UpAxis),
                r.FlangeSlope,
                r.FaceNormalThicknessMm,
                r.BisectorExtrusionMm,
                r.LeftPlaneRmsMm,
                r.RightPlaneRmsMm,
                r.FilledSamplingCells,
                r.RemovedAmbiguousCells,
                r.RemovedSmallIslandCells,
                r.OccupiedCells)
            {
                InputMeasurementCount = r.InputMeasurementCount,
                WingPlaneMeasurementCount = r.WingPlaneMeasurementCount,
                MeasuredRidgePointCount = r.MeasuredRidgePointCount,
                OuterEdgesFilled = r.OuterEdgesFilled,
            };
        }
    }
}
