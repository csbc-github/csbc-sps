using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// Workpiece selection only. The detector still observes the original full scan;
/// neither coordinates nor opening closure are changed by this membership mask.
/// </summary>
internal sealed class AngleSteelHoleScope
{
    private const double MaximumPlaneRmsMm = .5;
    private const double MinimumMembership = .95;
    private static readonly double MinimumNormalAlignment = Math.Cos(5 * Math.PI / 180);
    private readonly int[] pointOwners;
    private readonly PlaneFit[][] workpiecePlanes;

    private AngleSteelHoleScope(bool[] allowedSourcePoints, int[] owners, PlaneFit[][] planes,
        HoleScopeDiagnostics diagnostics)
    {
        AllowedSourcePoints = allowedSourcePoints;
        pointOwners = owners;
        workpiecePlanes = planes;
        AllowedWingPlanes = planes.SelectMany(p => p).ToArray();
        Diagnostics = diagnostics;
    }

    internal bool[] AllowedSourcePoints { get; }
    internal PlaneFit[] AllowedWingPlanes { get; }
    internal HoleScopeDiagnostics Diagnostics { get; }

    internal static AngleSteelHoleScope Build(float[] fullSourceXyz, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fullSourceXyz);
        if (fullSourceXyz.Length % 3 != 0) throw new ArgumentException("XYZ array length must be a multiple of three.", nameof(fullSourceXyz));
        int count = fullSourceXyz.Length / 3;
        var allowed = new bool[count];
        var owners = new int[count];
        var planeGroups = new List<PlaneFit[]>();
        var workpieces = new List<HoleScopeWorkpieceDiagnostic>();
        var failures = new List<string>();
        if (count < 500) return Empty("不足 500 個原始量測點，未確認可用的兩翼角鋼。", false);

        var source = new SourceScanPoint[count];
        for (int i = 0; i < count; i++)
        {
            if ((i & 65535) == 0) ct.ThrowIfCancellationRequested();
            float x = fullSourceXyz[i * 3], y = fullSourceXyz[i * 3 + 1], z = fullSourceXyz[i * 3 + 2];
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new ArgumentException("Source XYZ contains a nonfinite measurement.", nameof(fullSourceXyz));
            source[i] = new SourceScanPoint(i, new ScanVector(x, y, z), "");
        }
        var cloud = new ScanCloud([], source);
        var options = new ExtractionOptions(12, 3, 50) { RecoverMeasuredEdges = true };
        ExtractionResult extraction;
        try { extraction = AngleSteelExtractor.Process(cloud, options, cancellationToken: ct); }
        catch (CalibrationMathException ex)
        {
            return Empty("未取得可靠的工件與地面分離依據，未記錄角鋼孔洞。 " + ex.Message, false);
        }
        bool reliable = extraction.Calibration.GroundDiagnostics?.Reliable == true;
        if (!reliable) return Empty("未取得足夠可靠的地面支持，未將背景開口記為角鋼孔洞。", false);

        foreach (var component in extraction.Objects)
        {
            ct.ThrowIfCancellationRequested();
            var rawIndices = component.AbovePointIndices.Select(i => extraction.AbovePoints[i].SourcePointIndex).ToArray();
            var points = rawIndices.Select(i => source[i].Position).ToArray();
            if (points.Length < 500) continue;
            var candidates = WingPlaneSegmentation.FindPlanes(points,
                Math.Clamp(MaximumPlaneRmsMm * 1.5, .25, 1), 4, ct);
            GeometryFitting.TryFitWings(points, rawIndices, MaximumPlaneRmsMm,
                out var fit, out var reason, ct, candidates);
            if (fit is not null)
            {
                Add(fit, component.Rank, false);
                continue;
            }

            failures.Add(reason ?? "未辨識兩翼。");
            bool planar = false;
            try
            {
                var plane = GeometryFitting.Plane(points);
                planar = plane.Rms <= MaximumPlaneRmsMm && plane.Indices.Length >= points.Length * .65;
                if (!planar)
                {
                    double upper = GeometryFitting.Quantile(points.Select(p => p.Z), .6);
                    int[] top = Enumerable.Range(0, points.Length).Where(i => points[i].Z >= upper).ToArray();
                    var topPlane = GeometryFitting.Plane(points, top);
                    planar = topPlane.Rms <= MaximumPlaneRmsMm && topPlane.Indices.Length >= top.Length * .65
                        && Math.Abs(topPlane.Normal.Z) > .7;
                }
            }
            catch (InvalidOperationException) { }
            if (!planar) continue;
            var separated = LocalSupportPlaneSeparation.Find(cloud, points, rawIndices,
                extraction.Calibration, options, MaximumPlaneRmsMm, ct, candidates);
            foreach (var child in separated.Children) Add(child.Fit, component.Rank, true);
            if (separated.Diagnostics.Reason is { } supportReason) failures.Add(supportReason);
        }

        return new AngleSteelHoleScope(allowed, owners, planeGroups.ToArray(), new HoleScopeDiagnostics
        {
            WorkpieceIdentified = workpieces.Count > 0,
            Reason = workpieces.Count > 0 ? null : "未確認有足夠兩翼實測支持的角鋼；背景開口未記錄。"
                + (failures.Count > 0 ? " " + failures[0] : ""),
            SourcePointCount = count, GroundReliable = reliable,
            ValidatedWorkpieceCount = workpieces.Count,
            AllowedSourcePointCount = allowed.Count(x => x), AllowedWingPlaneCount = planeGroups.Count * 2,
            Workpieces = workpieces.ToArray(),
        });

        void Add(WingFit wing, int parentRank, bool localSupport)
        {
            int owner = workpieces.Count + 1;
            foreach (int index in wing.RawIndices)
            {
                if ((uint)index >= count) throw new InvalidDataException("Workpiece membership is outside the source scan.");
                allowed[index] = true;
                if (owners[index] == 0) owners[index] = owner;
            }
            PlaneFit RawPlane(PlaneFit plane) => new()
            {
                Normal = plane.Normal, D = plane.D, Rms = plane.Rms,
                Indices = plane.Indices.Select(i => wing.RawIndices[i]).ToArray(),
            };
            planeGroups.Add([RawPlane(wing.Left), RawPlane(wing.Right)]);
            workpieces.Add(new HoleScopeWorkpieceDiagnostic
            {
                WorkpieceNumber = owner, ParentComponentRank = parentRank,
                SeparatedFromLocalSupport = localSupport, MeasuredPointCount = wing.RawIndices.Length,
                WingNormalAngleDegrees = wing.NormalAngle,
                LeftPlaneRmsMm = wing.Left.Rms, RightPlaneRmsMm = wing.Right.Rms,
            });
        }

        AngleSteelHoleScope Empty(string reason, bool groundReliable) => new(allowed, owners, [],
            new HoleScopeDiagnostics { SourcePointCount = count, Reason = reason, GroundReliable = groundReliable });
    }

    /// <summary>Each opening must belong to one workpiece and to one of that workpiece's measured wings.</summary>
    internal bool Accept(IEnumerable<int> originalSourceIndices, HoleFace face)
    {
        if (face.Normal.Length != 3 || face.Origin.Length != 3) return false;
        var normal = new ScanVector(face.Normal[0], face.Normal[1], face.Normal[2]);
        var origin = new ScanVector(face.Origin[0], face.Origin[1], face.Origin[2]);
        double normalLength = Math.Sqrt(normal.Dot(normal));
        if (!double.IsFinite(normalLength) || normalLength <= 0
            || !double.IsFinite(origin.X) || !double.IsFinite(origin.Y) || !double.IsFinite(origin.Z)) return false;
        normal = normal.Scale(1 / normalLength);
        var counts = new Dictionary<int, int>();
        int total = 0;
        foreach (int sourceIndex in originalSourceIndices)
        {
            total++;
            if ((uint)sourceIndex >= pointOwners.Length) return false;
            int owner = pointOwners[sourceIndex];
            if (owner > 0) counts[owner] = counts.GetValueOrDefault(owner) + 1;
        }
        if (total == 0) return false;
        foreach (var (owner, supported) in counts)
        {
            if (supported < total * MinimumMembership) continue;
            foreach (var plane in workpiecePlanes[owner - 1])
                if (Math.Abs(normal.Dot(plane.Normal)) >= MinimumNormalAlignment
                    && Math.Abs(plane.Normal.Dot(origin) - plane.D) <= Math.Max(1.5, plane.Rms * 3))
                    return true;
        }
        return false;
    }
}
