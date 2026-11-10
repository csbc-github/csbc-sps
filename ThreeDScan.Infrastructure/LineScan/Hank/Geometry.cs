using System.Text.Json;
namespace ThreeDScan.Infrastructure.LineScan.Hank;
public readonly record struct P(double X, double Y)
{
    public static P operator +(P a, P b) => new(a.X + b.X, a.Y + b.Y);
    public static P operator -(P a, P b) => new(a.X - b.X, a.Y - b.Y);
    public static P operator *(P a, double b) => new(a.X * b, a.Y * b);
    public double Dot(P b) => X * b.X + Y * b.Y;
    public double Cross(P b) => X * b.Y - Y * b.X;
    public double Length => Math.Sqrt(Dot(this));
    public double Distance(P b) => (this - b).Length;
    public bool Finite => double.IsFinite(X) && double.IsFinite(Y);
}
public record Calibration(P First, P Second)
{
    public void Validate() { if (!First.Finite || !Second.Finite || First.Distance(Second) < 10 || Math.Abs(Transform(new()).Y) < 1e-6) throw new ArgumentException("桌面校正需兩個相距至少 10 mm 的有效點，且不可通過掃描器"); }
    public P Transform(P p) { var d = (Second - First) * (1 / First.Distance(Second)); var q = p - First; var flip = First.X * d.Y - First.Y * d.X >= 0 ? 1 : -1; return new(q.Dot(d), flip * d.Cross(q)); }
}
public record Line(List<P> Points, P Center, P Direction, double Rms, P Start, P End)
{
    public P Far(P corner) => Start.Distance(corner) > End.Distance(corner) ? Start : End;
    public static Line Fit(List<P> p) { var c = new P(p.Average(v => v.X), p.Average(v => v.Y)); double xx = 0, yy = 0, xy = 0; foreach (var v in p) { var q = v - c; xx += q.X * q.X; yy += q.Y * q.Y; xy += q.X * q.Y; } var t = .5 * Math.Atan2(2 * xy, xx - yy); var d = new P(Math.Cos(t), Math.Sin(t)); if ((p[^1] - p[0]).Dot(d) < 0) d = d * -1; var ds = p.Select(v => (v - c).Dot(d)).ToArray(); return new(p, c, d, Math.Sqrt(p.Average(v => Math.Pow(d.Cross(v - c), 2))), c + d * ds.Min(), c + d * ds.Max()); }
    public P? Intersect(Line b) { var den = Direction.Cross(b.Direction); return Math.Abs(den) < 1e-9 ? null : Center + Direction * ((b.Center - Center).Cross(b.Direction) / den); }
}
public record ProfileResult(P[] Points, double LeftMm, double RightMm, double WidthMm, double HeightMm, double CorrectedHeightMm, double HeightCompensationMm)
{
    public P HeightPoint { get; init; }
    public double FittedWidthMm { get; init; }
    public double FittedLeftMm { get; init; }
    public double FittedRightMm { get; init; }
    public double FittedHeightMm { get; init; }
    public double RawWidthMm { get; init; }
    public double OriginalLeftMm { get; init; }
    public double OriginalRightMm { get; init; }
    public double[] EndpointAdjustmentsMm { get; init; } = [0, 0];
    public bool EndpointCleaning { get; init; }
    public bool RecoverSparse { get; init; }
    public string EndpointSelection { get; init; } = "nearest_fitted_end";
    public string Method { get; init; } = "raw";
    public string FitStatus { get; init; } = "raw";
    public double[] RecoveredGapsMm { get; init; } = [];
    public string[] Warnings { get; init; } = [];
    public bool AutoCalibration { get; init; }
    public string? AutoCalibrationModel { get; init; }
    public bool AutoFaceReliable { get; init; }
    public P[]? LegPoints { get; init; }
    public double? RawLeftMm { get; init; }
    public double? RawRightMm { get; init; }
    public double? RawMeasuredWidthMm { get; init; }
    public double? RawHeightMm { get; init; }
    public bool DimensionCorrection { get; init; }
    public double DimensionScaleX { get; init; } = 1;
    public double DimensionScaleHeight { get; init; } = 1;
    public bool HasHeightCompensation { get; init; }
    public string[] AutoEndpointActions { get; init; } = [];

    public Dictionary<string, object?> ToPythonReport()
    {
        static double[] Point(P p) => [p.X, p.Y];
        var result = new Dictionary<string, object?>
        {
            ["points"] = Points.Select(Point).ToArray(), ["left_mm"] = LeftMm, ["right_mm"] = RightMm,
            ["width_mm"] = WidthMm, ["height_mm"] = HeightMm, ["height_point"] = Point(HeightPoint),
            ["fitted_width_mm"] = FittedWidthMm, ["fitted_left_mm"] = FittedLeftMm, ["fitted_right_mm"] = FittedRightMm,
            ["fitted_height_mm"] = FittedHeightMm, ["raw_width_mm"] = RawWidthMm,
            ["original_left_mm"] = OriginalLeftMm, ["original_right_mm"] = OriginalRightMm,
            ["endpoint_adjustments_mm"] = EndpointAdjustmentsMm, ["endpoint_cleaning"] = EndpointCleaning,
            ["recover_sparse"] = RecoverSparse, ["recovered_gaps_mm"] = RecoveredGapsMm, ["endpoint_selection"] = EndpointSelection,
            ["method"] = Method, ["fit_status"] = FitStatus, ["warnings"] = Warnings
        };
        if (AutoCalibration || DimensionCorrection || HasHeightCompensation)
        {
            result["corrected_height_mm"] = CorrectedHeightMm;
            result["height_compensation_mm"] = HeightCompensationMm;
        }
        if (AutoCalibration || DimensionCorrection)
        {
            result["raw_left_mm"] = RawLeftMm; result["raw_right_mm"] = RawRightMm;
            result["raw_measured_width_mm"] = RawMeasuredWidthMm; result["raw_height_mm"] = RawHeightMm;
        }
        if (AutoCalibration)
        {
            result["auto_calibration"] = true; result["auto_calibration_model"] = AutoCalibrationModel;
            result["auto_face_reliable"] = AutoFaceReliable; result["auto_endpoint_actions"] = AutoEndpointActions;
            result["leg_points"] = LegPoints?.Select(Point).ToArray();
        }
        if (DimensionCorrection)
        {
            result["dimension_correction"] = true; result["dimension_scale_x"] = DimensionScaleX;
            result["dimension_scale_height"] = DimensionScaleHeight;
        }
        return result;
    }
}
public static class Profile
{
    public const double TrialScaleX = 1.0295;
    public const double TrialScaleHeight = .987;
    public const double TrialHeightOffsetMm = 6.0;
    public const double AutoHeightOffsetMm = 1.9528341984012556;
    public const string AutoCalibrationModel = "bounded_face_blend_v2";

    static double Median(IEnumerable<double> values) { var a = values.Order().ToArray(); return (a[(a.Length-1)/2]+a[a.Length/2])/2; }
    public static Line FitFace(List<P> points)
    {
        var ordered = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        int trim = Math.Max(1, (int)(ordered.Count * .15));
        var core = ordered.Count - 2*trim >= 4 ? ordered.GetRange(trim,ordered.Count-2*trim) : ordered;
        for (int i = 0; i < 3; i++)
        {
            var line = Line.Fit(core); var residuals = core.Select(p => line.Direction.Cross(p-line.Center)).ToArray();
            double center = Median(residuals), mad = Median(residuals.Select(v => Math.Abs(v-center)));
            var keep = core.Where((p,j) => Math.Abs(residuals[j]-center) <= Math.Max(.5,3*1.4826*mad)).ToList();
            if (keep.Count < 4 || keep.Count == core.Count) break;
            core = keep;
        }
        return Line.Fit(core);
    }
    public static P? MixedTableTransition(Line face, Line line, Line? table, P end)
    {
        if (table == null || Math.Abs(table.Direction.Y)>=.1 || table.Rms>=1 || Math.Abs(line.Direction.Y)<.8 || table.Points.Min(p=>p.Distance(end))>12) return null;
        var ordered=face.Points.OrderBy(p=>p.Distance(end)).ToArray();
        if (ordered.Length<4 || ordered.Take(2).Max(p=>Math.Abs(p.Y))>4) return null;
        var residuals=ordered.Select(p=>line.Direction.Cross(p-line.Center)).ToArray();
        double limit=Math.Max(1,3*line.Rms); int mixedPair=-1;
        for (int i=0;i<Math.Min(2,ordered.Length-3);i++)
            if (Math.Max(Math.Abs(ordered[i].Y),Math.Abs(ordered[i+1].Y))<=4 && Math.Min(Math.Abs(residuals[i]),Math.Abs(residuals[i+1]))>limit && residuals[i]*residuals[i+1]>0) { mixedPair=i; break; }
        if (mixedPair<0) return null;
        for (int i=mixedPair+2;i<Math.Min(5,ordered.Length-1);i++)
            if (Math.Abs(residuals[i])<=limit && Math.Abs(residuals[i+1])<=limit && ordered[i].Y>ordered[1].Y && end.Distance(ordered[i])<=16)
                return end+(ordered[i]-end)*Math.Min(.5,4/end.Distance(ordered[i]));
        return null;
    }
    public static ProfileResult AutoCorrect(ProfileResult report, Calibration calibration, Line[] faces, Line?[] tables, string model = AutoCalibrationModel)
    {
        var raw = report.Points.Select(calibration.Transform).ToArray(); var peak = raw[1]; var ends = new[] {raw[0],raw[2]};
        var lines = faces.Select(f => FitFace(f.Points)).ToArray(); var top = lines[0].Intersect(lines[1]);
        bool reliable = top is P t && t.X >= ends[0].X-2 && t.X <= ends[1].X+2 && t.Y > 0 && t.Distance(peak) <= 10 && lines.Max(l => l.Rms) <= 2;
        string[] actions = ["raw","raw"];
        if (reliable)
        {
            peak = (peak + top!.Value) * .5;
            for (int i = 0; i < 2; i++)
            {
                var end = ends[i]; var line = lines[i]; var table = tables[i];
                if (model == AutoCalibrationModel && MixedTableTransition(faces[i],line,table,end) is P transition)
                { ends[i]=transition; actions[i]="supported_table_transition_blend"; continue; }
                var ordered = faces[i].Points.OrderBy(p => p.Distance(end)).ToArray(); var delta = ordered[1]-ordered[0];
                double cosine = delta.Length > 0 ? Math.Abs(delta.Dot(line.Direction))/delta.Length : 1;
                double angle = Math.Acos(Math.Min(1,cosine))*180/Math.PI;
                if (table != null && Math.Abs(table.Direction.Y)<.1 && table.Rms<1 && table.Points.Min(p => p.Distance(end))<=12 && Math.Max(Math.Abs(ordered[0].Y),Math.Abs(ordered[1].Y))<=3 && angle>15 && delta.Length>0 && delta.Length<=8)
                { ends[i]=ordered[1]; actions[i]="table_transition_removed"; }
                else if (Math.Abs(line.Direction.Y)>=1e-6)
                {
                    var landed = new P(line.Center.X-line.Center.Y*line.Direction.X/line.Direction.Y,0);
                    if (Math.Abs(end.Y)<=4 && landed.Distance(end)<=10) { ends[i]=(end+landed)*.5; actions[i]="bounded_table_intersection_blend"; }
                }
            }
        }
        var d=(calibration.Second-calibration.First)*(1/calibration.First.Distance(calibration.Second));
        double flip=calibration.First.X*d.Y-calibration.First.Y*d.X>=0?1:-1;
        P Inverse(P p) => calibration.First+new P(p.X*d.X-flip*p.Y*d.Y,p.X*d.Y+flip*p.Y*d.X);
        return report with { AutoCalibration=true, AutoCalibrationModel=model, AutoFaceReliable=reliable,
            RawLeftMm=report.LeftMm, RawRightMm=report.RightMm, RawMeasuredWidthMm=report.WidthMm, RawHeightMm=report.HeightMm, AutoEndpointActions=actions,
            LeftMm=ends[0].Distance(peak), RightMm=ends[1].Distance(peak),
            LegPoints=[Inverse(ends[0]),Inverse(peak),Inverse(ends[1])],
            CorrectedHeightMm=report.HeightMm+AutoHeightOffsetMm, HeightCompensationMm=AutoHeightOffsetMm,
            Warnings=reliable ? report.Warnings : [..report.Warnings,"斜邊擬合未通過自動校正檢查，保留原始邊長"] };
    }

    public static List<ProfileResult> CorrectDimensions(IEnumerable<ProfileResult> reports, Calibration calibration, double scaleX = 1, double scaleHeight = 1, double offsetHeightMm = 0)
    {
        calibration.Validate();
        if (!double.IsFinite(scaleX) || !double.IsFinite(scaleHeight) || !double.IsFinite(offsetHeightMm) || scaleX <= 0 || scaleHeight <= 0)
            throw new ArgumentException("尺寸倍率必須是大於 0 的有限數值，高度補償也必須是有限數值");
        var corrected = new List<ProfileResult>();
        foreach (var report in reports)
        {
            var p = report.Points.Select(calibration.Transform).Select(v => new P(v.X * scaleX, v.Y * scaleHeight)).ToArray();
            var height = report.HeightMm * scaleHeight + offsetHeightMm;
            if (height <= 0) throw new ArgumentException("校正後高度必須大於 0");
            corrected.Add(report with
            {
                LeftMm = p[0].Distance(p[1]),
                RightMm = p[1].Distance(p[2]),
                WidthMm = p[2].X - p[0].X,
                CorrectedHeightMm = height,
                HeightCompensationMm = offsetHeightMm
                , RawLeftMm = report.LeftMm, RawRightMm = report.RightMm, RawMeasuredWidthMm = report.WidthMm, RawHeightMm = report.HeightMm,
                DimensionCorrection = true, DimensionScaleX = scaleX, DimensionScaleHeight = scaleHeight
            });
        }
        return corrected;
    }

    public static bool SparseEdgeBridge(List<P> points, int index, double gap = 12)
    {
        double distance = points[index - 1].Distance(points[index]);
        if (distance <= gap || distance > 24 || index < 2 || index + 3 > points.Count) return false;
        var support = points.GetRange(index - 2, 5); var line = Line.Fit(support);
        if (Math.Abs(line.Direction.Y) < .85 || line.Rms > 1 || support.Max(p => Math.Abs((p - line.Center).Cross(line.Direction))) > 2) return false;
        var steps = Enumerable.Range(0, 4).Select(i => support[i + 1] - support[i]).ToArray();
        if (steps.Any(p => p.Length > 24)) return false;
        var projections = steps.Select(p => p.Dot(line.Direction)).ToArray();
        if (projections.Min() < -.5 && projections.Max() > .5) return false;
        return Math.Abs(steps[1].Dot(line.Direction)) / distance >= .98;
    }

    public static List<Line> Segments(List<P> points, double tolerance = 2, int min = 4, double gap = 12, bool recoverSparse = false, int trim = 0)
    {
        var runs = new List<List<P>>(); for (int i = 0; i < points.Count; i++) { var p = points[i]; if (i == 0 || points[i-1].Distance(p) > gap && !(recoverSparse && SparseEdgeBridge(points, i, gap))) runs.Add([]); runs[^1].Add(p); }
        var result = new List<Line>();
        foreach (var run in runs)
        {
            var pieces = new List<(int A, int B)>(); var pending = new Stack<(int, int)>(); pending.Push((0, run.Count - 1));
            while (pending.Count > 0) { var (a, b) = pending.Pop(); if (b - a + 1 < 2 * min) { pieces.Add((a, b)); continue; } double worst = 0; int at = -1; var delta = run[b] - run[a]; for (int i = a + 1; i < b; i++) { double e = delta.Length < 1e-9 ? run[i].Distance(run[a]) : Math.Abs(delta.Cross(run[i] - run[a])) / delta.Length; if (e > worst) { worst = e; at = i; } } if (worst > tolerance && at - a >= min && b - at >= min) { pending.Push((a, at)); pending.Push((at, b)); } else pieces.Add((a, b)); }
            pieces = pieces.Where(v => v.B - v.A + 1 >= min).OrderBy(v => v.A).ToList();
            while (pieces.Count > 1) { double best = double.PositiveInfinity; int at = -1; for (int i = 0; i < pieces.Count - 1; i++) { var a = pieces[i]; var b = pieces[i + 1]; if (b.A - a.B > 1) continue; var rms = Line.Fit(run.GetRange(a.A, b.B - a.A + 1)).Rms; if (rms <= tolerance && rms < best) { best = rms; at = i; } } if (at < 0) break; pieces[at] = (pieces[at].A, pieces[at + 1].B); pieces.RemoveAt(at + 1); }
            for (int pass = 0; pass < 2; pass++) { bool moved = false; for (int i = 0; i < pieces.Count - 1; i++) { var a = pieces[i]; var b = pieces[i + 1]; if (b.A != a.B) continue; double best = double.PositiveInfinity; int at = a.B; for (int k = a.B - 3; k <= a.B + 3; k++) { if (k - a.A + 1 < min || b.B - k + 1 < min) continue; var l = Line.Fit(run.GetRange(a.A, k - a.A + 1)); var r = Line.Fit(run.GetRange(k, b.B - k + 1)); var e = l.Rms * l.Rms * l.Points.Count + r.Rms * r.Rms * r.Points.Count; if (e < best) { best = e; at = k; } } if (at != a.B) { pieces[i] = (a.A, at); pieces[i + 1] = (at, b.B); moved = true; } } if (!moved) break; }
            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                int low = piece.A + (i > 0 && piece.A - pieces[i - 1].B <= 1 ? trim : 0);
                int high = piece.B - (i + 1 < pieces.Count && pieces[i + 1].A - piece.B <= 1 ? trim : 0);
                if (high - low + 1 < min) { low = piece.A; high = piece.B; }
                result.Add(Line.Fit(run.GetRange(low, high - low + 1)));
            }
        }
        return result;
    }
    public static P CleanTableEndpoint(Line leg, Line? table, P endpoint)
    {
        if (table == null || Math.Abs(table.Direction.Y) >= .1 || table.Rms >= 1) return endpoint;
        if (table.Points.Min(p => p.Distance(endpoint)) > 12) return endpoint;
        var nearby = table.Points.OrderBy(p => Math.Abs(p.X - endpoint.X)).Skip(1).Take(8).ToArray();
        if (nearby.Length < 4) return endpoint;
        double Median(IEnumerable<double> values) { var a = values.Order().ToArray(); return (a[(a.Length - 1) / 2] + a[a.Length / 2]) / 2; }
        double floor = Median(nearby.Select(p => p.Y));
        if (Math.Abs(floor) >= 4) return endpoint;
        double noise = Math.Max(1, 3 * 1.4826 * Median(nearby.Select(p => Math.Abs(p.Y - floor))));
        var ordered = leg.Points.OrderBy(p => p.Distance(endpoint)).ToArray();
        int count = ordered.TakeWhile(p => p.Y < floor + noise - 1e-9).Count();
        return count >= 2 && count < ordered.Length - 3 && endpoint.Distance(ordered[count]) <= 8 ? ordered[count] : endpoint;
    }
    static P Foot(Line line, P observed)
    {
        if (Math.Abs(line.Direction.Y) < 1e-6) throw new ArgumentException("斜邊方向不明確");
        var landed = new P(line.Center.X - line.Center.Y * line.Direction.X / line.Direction.Y, 0);
        if (Math.Abs(observed.Y) > 4 || landed.Distance(observed) > 10) throw new ArgumentException("斜邊未接近原桌面或端點偏移過大，請檢查遮擋與桌面校正");
        return landed;
    }
    public static List<ProfileResult> Measure(IEnumerable<P> source, Calibration calibration, double correction = 0, bool cleanTable = false, bool recoverSparse = true, bool autoCalibrate = false, string autoModel = AutoCalibrationModel)
    {
        if (autoCalibrate) { cleanTable=false; recoverSparse=true; correction=0; }
        calibration.Validate(); if (!double.IsFinite(correction)) throw new ArgumentException("高度補償無效"); var pairs = source.Where(p => p.Finite).Select(p => (Level: calibration.Transform(p), Raw: p)).OrderBy(p => p.Level.X).ThenBy(p => p.Level.Y).ToList(); if (pairs.Count < 8) throw new ArgumentException("掃描點不足");
        if (recoverSparse)
        {
            var groups = pairs.GroupBy(p => p.Level.X).Select(g => g.ToList()).ToList(); pairs = [];
            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group.Count > 1)
                {
                    if (pairs.Count > 0) { var anchor = pairs[^1].Level; if (anchor.Distance(group[^1].Level) < anchor.Distance(group[0].Level)) group.Reverse(); }
                    else if (i + 1 < groups.Count) { var anchor = groups[i+1][0].Level; if (anchor.Distance(group[0].Level) < anchor.Distance(group[^1].Level)) group.Reverse(); }
                }
                pairs.AddRange(group);
            }
        }
        var points = pairs.Select(p => p.Level).ToList(); var segments = Segments(points, recoverSparse: recoverSparse); var results = new List<ProfileResult>(); double last = double.NegativeInfinity;
        for (int i = 0; i < segments.Count - 1; i++)
        {
            var l = segments[i]; var r = segments[i + 1]; if (l.Points[^1].Distance(r.Points[0]) > 12) continue; double angle = Math.Acos(Math.Clamp(Math.Abs(l.Direction.Dot(r.Direction)), -1, 1)) * 180 / Math.PI; var apex = l.Intersect(r); if (apex is not P top || Math.Abs(angle - 90) > 30 || top.Y < 5) continue; var lf = l.Far(top); var rf = r.Far(top);
            if (recoverSparse ? top.Y < Math.Max(lf.Y, rf.Y) - 2 || top.Y - Math.Min(lf.Y, rf.Y) < 5 : top.Y <= Math.Max(lf.Y, rf.Y)) continue;
            double Leg(Line line, P end) { if (Math.Abs(end.Y) <= 3 && Math.Abs(line.Direction.Y) > 1e-3) end += line.Direction * (-end.Y / line.Direction.Y); return top.Distance(end); }
            double ll = Leg(l, lf), rr = Leg(r, rf); if (Math.Min(ll, rr) < 10 || Math.Max(ll, rr) > 1000 || Math.Max(l.End.Distance(top), r.Start.Distance(top)) > .35 * Math.Max(ll, rr) + 10) continue;
            bool upright = recoverSparse && Math.Max(Math.Abs(l.Direction.Y), Math.Abs(r.Direction.Y)) >= .95;
            var a = upright ? l.Points.MaxBy(p => p.Dot(lf - top)) : l.Points.MinBy(p => p.Distance(lf));
            var c = upright ? r.Points.MaxBy(p => p.Dot(rf - top)) : r.Points.MinBy(p => p.Distance(rf));
            var originalA = a; var originalC = c;
            if(cleanTable){a=CleanTableEndpoint(l,i>0?segments[i-1]:null,a);c=CleanTableEndpoint(r,i+2<segments.Count?segments[i+2]:null,c);}
            if (a.X < last || c.X <= a.X) continue;
            var samples = pairs.Where(p => p.Level.X >= a.X && p.Level.X <= c.X).ToList(); var highest = samples.MaxBy(p => p.Level.Y);
            var peak = upright ? samples.MinBy(p => p.Level.Distance(top)) : highest;
            if (peak.Level.X < a.X || peak.Level.X > c.X || Math.Min(a.Distance(peak.Level), c.Distance(peak.Level)) < 1e-6) continue;
            if (highest.Level.Y + correction <= 0) throw new ArgumentException("補償高度不可小於零");
            var gaps = Enumerable.Range(1, points.Count - 1).Where(k => recoverSparse && points[k-1].X >= a.X && points[k-1].X <= c.X && points[k].X >= a.X && points[k].X <= c.X && SparseEdgeBridge(points, k)).Select(k => points[k-1].Distance(points[k])).ToArray();
            var warnings = new List<string>();
            if (gaps.Length > 0) warnings.Add($"已保留稀疏直邊掃描點（最大點距 {gaps.Max().ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} mm）；邊長仍依可見端點量測");
            if (Math.Max(a.Y, c.Y) > 4) warnings.Add("端點高於桌面：顯示可見邊長，未外推到底線");
            P fittedA, fittedPeak, fittedC; string fitStatus;
            try
            {
                var firstFit = FitFace(l.Points); var secondFit = FitFace(r.Points); var crossing = firstFit.Intersect(secondFit);
                fittedA = Foot(firstFit, a); fittedC = Foot(secondFit, c);
                if (crossing is not P fitted || !(fittedA.X < fitted.X && fitted.X < fittedC.X) || fitted.Y <= 0 || fitted.Distance(peak.Level) > 10 || Math.Max(firstFit.Rms, secondFit.Rms) > 2)
                    throw new ArgumentException("斜邊擬合不穩定，保留原始選點結果");
                fittedPeak = fitted; fitStatus = "fitted";
            }
            catch (ArgumentException error)
            {
                fittedA = a; fittedPeak = peak.Level; fittedC = c; fitStatus = "raw";
                if (!upright || Math.Max(a.Y, c.Y) <= 4) warnings.Add(error.Message);
            }
            if (Math.Abs(peak.Level.Y - fittedPeak.Y) > 2) warnings.Add("最高點與擬合頂點差超過 2 mm，請檢查頂點雜訊");
            results.Add(new([samples.MinBy(p => p.Level.Distance(a)).Raw, peak.Raw, samples.MinBy(p => p.Level.Distance(c)).Raw], a.Distance(peak.Level), c.Distance(peak.Level), c.X - a.X, highest.Level.Y, highest.Level.Y + correction, correction)
            {
                HeightPoint = highest.Raw, FittedWidthMm = fittedC.X - fittedA.X, FittedLeftMm = fittedA.Distance(fittedPeak), FittedRightMm = fittedPeak.Distance(fittedC), FittedHeightMm = fittedPeak.Y,
                RawWidthMm = originalC.X - originalA.X, OriginalLeftMm = originalA.Distance(peak.Level), OriginalRightMm = peak.Level.Distance(originalC),
                EndpointAdjustmentsMm = [originalA.Distance(a), originalC.Distance(c)], EndpointCleaning = cleanTable, RecoverSparse = recoverSparse,
                EndpointSelection = upright ? "observed_face_extent" : "nearest_fitted_end", Method = a != originalA || c != originalC ? "table_cleaned" : "raw",
                FitStatus = fitStatus, RecoveredGapsMm = gaps, Warnings = warnings.ToArray(), HasHeightCompensation = correction != 0
            }); last = c.X;
            if (autoCalibrate) results[^1]=AutoCorrect(results[^1],calibration,[l,r],[i>0?segments[i-1]:null,i+2<segments.Count?segments[i+2]:null],autoModel);
        }
        if (results.Count == 0) throw new ArgumentException("未辨識到完整角鋼截面"); return results;
    }
    public static (P[] Points, Calibration Calibration, double Correction) ReadSnapshot(string path) { using var doc = JsonDocument.Parse(File.ReadAllText(path)); var root = doc.RootElement; P Parse(JsonElement a) => new(a[0].GetDouble(), a[1].GetDouble()); var c = root.GetProperty("calibration"); return (root.GetProperty("selected_points").EnumerateArray().Select(Parse).ToArray(), new(Parse(c.GetProperty("first")), Parse(c.GetProperty("second"))), root.TryGetProperty("height_compensation_mm", out var offset) ? offset.GetDouble() : 0); }
    public static List<ProfileResult> ReplaySnapshot(string path)
    {
        var (points, calibration, offset) = ReadSnapshot(path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path)); var root = doc.RootElement;
        bool clean = root.TryGetProperty("algorithm", out var algorithm) && algorithm.GetString() == "table_supported_endpoints_v3";
        if (root.TryGetProperty("endpoint_cleaning", out var endpointCleaning)) clean = endpointCleaning.GetBoolean();
        double sx = root.TryGetProperty("dimension_scale_x", out var x) ? x.GetDouble() : 1;
        double sz = root.TryGetProperty("dimension_scale_height", out var z) ? z.GetDouble() : 1;
        bool recoverSparse = root.TryGetProperty("recover_sparse", out var sparse) && sparse.GetBoolean();
        if (root.TryGetProperty("auto_calibration", out var automatic) && automatic.GetBoolean())
            return Measure(points, calibration, autoCalibrate: true,
                autoModel: root.TryGetProperty("auto_calibration_model", out var model) ? model.GetString() ?? "bounded_face_blend_v1" : "bounded_face_blend_v1");
        return CorrectDimensions(Measure(points, calibration, 0, clean, recoverSparse), calibration, sx, sz, offset);
    }
}
