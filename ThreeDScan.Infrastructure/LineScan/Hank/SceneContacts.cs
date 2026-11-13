namespace ThreeDScan.Infrastructure.LineScan.Hank;

public static partial class Profile
{
    static ProfileResult CorrectSceneContacts(ProfileResult report, Calibration calibration, Line[] faces, Line?[] tables, List<P> nativePoints)
    {
        var raw = report.Points.Select(calibration.Transform).ToArray();
        var peak = raw[1];
        P[] ends = [raw[0], raw[2]];
        var lines = faces.Select(f => FitFace(f.Points)).ToArray();
        var top = lines[0].Intersect(lines[1]);
        bool reliable = top is P fitted && fitted.X >= ends[0].X - 2 && fitted.X <= ends[1].X + 2
            && fitted.Y > 0 && fitted.Distance(peak) <= 10 && lines.Max(l => l.Rms) <= 2;
        string[] actions = ["raw", "raw"];
        double?[] offsets = [null, null];
        bool[] rejectedTableContacts = [false, false];
        if (reliable)
        {
            peak = top!.Value;
            for (int i = 0; i < 2; i++)
            {
                var observed = raw[i * 2];
                if (SupportedSteepContact(lines[i], tables[i], observed) is P landed
                    && ChordSupported(lines[i], peak, landed))
                {
                    ends[i] = landed;
                    actions[i] = "supported_steep_baseline_contact";
                    continue;
                }
                var contact = SupportedTransitionContact(nativePoints, lines[i], observed, i);
                if (contact is { } supported && ChordSupported(lines[i], peak, supported.Point))
                {
                    ends[i] = supported.Point;
                    offsets[i] = supported.TableHeight;
                    actions[i] = "supported_scan_table_contact";
                    continue;
                }
                rejectedTableContacts[i] = contact != null;
                if (SupportedBodyEndpoint(nativePoints, lines[i], observed, peak, i) is P bodyEnd)
                {
                    ends[i] = bodyEnd;
                    actions[i] = "supported_body_projection";
                }
            }
        }
        var d = (calibration.Second - calibration.First) * (1 / calibration.First.Distance(calibration.Second));
        double flip = calibration.First.X * d.Y - calibration.First.Y * d.X >= 0 ? 1 : -1;
        P Inverse(P p) => calibration.First + new P(p.X * d.X - flip * p.Y * d.Y, p.X * d.Y + flip * p.Y * d.X);
        var warnings = report.Warnings.Where(w => !w.StartsWith("端點高於桌面", StringComparison.Ordinal)).ToList();
        for (int i = 0; i < 2; i++) if (actions[i] == "raw")
            warnings.Add($"{(i == 0 ? "左" : "右")}端缺少接觸桌面的連續掃描支持，保留可見端點");
        if (rejectedTableContacts.Any(rejected => rejected)) warnings.Add("桌面接點超出斜面支持，保留可見斜邊");
        var chordErrors = Enumerable.Range(0, 2).Select(i => ChordErrors(lines[i], peak, ends[i])).ToArray();
        return report with
        {
            AutoCalibration = true, AutoCalibrationModel = SceneContactModel, AutoFaceReliable = reliable,
            RawLeftMm = report.LeftMm, RawRightMm = report.RightMm, RawWidthMm = report.WidthMm, RawMeasuredWidthMm = report.WidthMm, RawHeightMm = report.HeightMm,
            AutoEndpointActions = actions, TableContactOffsetsMm = offsets,
            FaceChordRmsMm = chordErrors.Select(e => e.Rms).ToArray(), FaceChordP95Mm = chordErrors.Select(e => e.P95).ToArray(),
            ApexFitShiftMm = peak.Distance(raw[1]),
            LeftMm = ends[0].Distance(peak), RightMm = ends[1].Distance(peak), WidthMm = ends[1].X - ends[0].X,
            LegPoints = [Inverse(ends[0]), Inverse(peak), Inverse(ends[1])],
            CorrectedHeightMm = peak.Y, HeightCompensationMm = peak.Y - report.HeightMm,
            Warnings = warnings.ToArray()
        };
    }

    static (double Rms, double P95) ChordErrors(Line face, P peak, P end)
    {
        var delta = end - peak;
        if (delta.Length < 1e-9) return (double.PositiveInfinity, double.PositiveInfinity);
        var direction = delta * (1 / delta.Length);
        var errors = face.Points.Select(p => Math.Abs(direction.Cross(p - peak))).Order().ToArray();
        double at = .95 * (errors.Length - 1);
        int lo = (int)at;
        double p95 = errors[lo] + (errors[Math.Min(lo + 1, errors.Length - 1)] - errors[lo]) * (at - lo);
        return (Math.Sqrt(errors.Average(e => e * e)), p95);
    }

    static bool ChordSupported(Line face, P peak, P end)
    {
        var errors = ChordErrors(face, peak, end);
        return errors.Rms <= Math.Max(2, 3 * face.Rms) && errors.P95 <= Math.Max(3, 4 * face.Rms);
    }

    internal static P? SupportedBodyEndpoint(List<P> nativePoints, Line face, P observed, P peak, int side)
    {
        if (nativePoints.Count == 0) return null;
        if (side == 0)
        {
            nativePoints = nativePoints.AsEnumerable().Reverse().Select(p => new P(-p.X, p.Y)).ToList();
            observed = new(-observed.X, observed.Y);
            peak = new(-peak.X, peak.Y);
            face = face with
            {
                Center = new(-face.Center.X, face.Center.Y), Direction = new(-face.Direction.X, face.Direction.Y),
                Points = face.Points.Select(p => new P(-p.X, p.Y)).ToList()
            };
        }
        var anchor = face.Points.MaxBy(p => p.Distance(peak));
        int start = Enumerable.Range(0, nativePoints.Count).MinBy(i => nativePoints[i].Distance(anchor));
        P? last = null;
        int misses = 0;
        for (int i = start; i < nativePoints.Count; i++)
        {
            var point = nativePoints[i];
            if (point.X > observed.X + 60 || i > start && nativePoints[i - 1].Distance(point) > 24) break;
            if (Math.Abs(face.Direction.Cross(point - face.Center)) <= 2)
            {
                last = point;
                misses = 0;
            }
            else if (++misses >= 2) break;
        }
        if (last is not P supported) return null;
        var projected = face.Center + face.Direction * (supported - face.Center).Dot(face.Direction);
        return side == 0 ? new P(-projected.X, projected.Y) : projected;
    }

    static P? SupportedSteepContact(Line face, Line? table, P end)
    {
        if (Math.Abs(face.Direction.Y) < .85 || Math.Abs(end.Y) > 4 || table == null
            || Math.Abs(table.Direction.Y) >= .1 || table.Rms >= 1
            || table.Points.Min(p => p.Distance(end)) > 12) return null;
        var landed = new P(face.Center.X - face.Center.Y * face.Direction.X / face.Direction.Y, 0);
        return landed.Distance(end) <= 10 ? landed : null;
    }

    internal static (P Point, double TableHeight)? SupportedTransitionContact(List<P> nativePoints, Line face, P end, int side = 1)
    {
        if (nativePoints.Count < 6) return null;
        if (side == 0)
        {
            nativePoints = nativePoints.AsEnumerable().Reverse().Select(p => new P(-p.X, p.Y)).ToList();
            end = new(-end.X, end.Y);
            face = face with { Center = new(-face.Center.X, face.Center.Y), Direction = new(-face.Direction.X, face.Direction.Y) };
        }
        int endIndex = Enumerable.Range(0, nativePoints.Count).MinBy(i => nativePoints[i].Distance(end));
        int start = Math.Max(0, endIndex - 4);
        var candidates = new List<P>();
        for (int i = start; i < nativePoints.Count; i++)
        {
            if (nativePoints[i].X > end.X + 60) break;
            candidates.Add(nativePoints[i]);
        }
        var floorPoints = candidates.Skip(endIndex - start).Where(p => Math.Abs(p.Y) <= 4).ToList();
        if (floorPoints.Count < 4) return null;
        var table = FitFace(floorPoints);
        if (table.Points.Count < 4 || Math.Abs(table.Direction.Y) >= .1 || table.Rms >= 1) return null;
        double Residual(Line line, P p) => Math.Abs(line.Direction.Cross(p - line.Center));
        double band = Math.Max(1, 3 * table.Rms);
        int stable = -1;
        for (int i = 2; i + 4 <= candidates.Count; i++)
        {
            if (Residual(table, candidates[i]) <= band
                && candidates.Skip(i).Take(4).Count(p => Residual(table, p) <= band) >= 3)
            { stable = i; break; }
        }
        if (stable < 0) return null;
        var transition = Line.Fit(candidates.GetRange(stable - 2, 3));
        if (Math.Abs(transition.Direction.Y) <= .5 || transition.Rms > 1) return null;
        var crossing = transition.Intersect(table);
        if (crossing is not P contact || !contact.Finite || Math.Abs(contact.Y) > 4
            || contact.X < Math.Min(candidates[stable - 1].X, candidates[stable].X)
            || contact.X > Math.Max(candidates[stable - 1].X, candidates[stable].X)) return null;
        // Use the same 2 mm support tolerance as the original face segmentation.
        double faceBand = Math.Max(2, 3 * face.Rms);
        int anchor = -1;
        for (int i = 0; i < stable; i++) if (Residual(face, candidates[i]) <= faceBand) anchor = i;
        if (anchor < 0 || candidates[anchor].Distance(contact) > 24) return null;
        double path = candidates[stable - 1].Distance(contact);
        for (int i = anchor + 1; i < stable; i++)
        {
            double gap = candidates[i - 1].Distance(candidates[i]);
            if (gap > 24) return null;
            path += gap;
        }
        if (candidates[stable - 1].Distance(candidates[stable]) > 24 || path > 24) return null;
        return (new P(side == 0 ? -contact.X : contact.X, 0), contact.Y);
    }
}
