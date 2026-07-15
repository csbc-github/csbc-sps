namespace ThreeDScan.Infrastructure.LjSdk;

using Keyence.LjDevCommon;
using Keyence.LjDevMeasure;
using ThreeDScan.Domain.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// LJ Developer 1.5.0.26460 — 對齊 source\ljd_measure.cs：PM.Execute → Sphere.Execute(DeltaPosition)

internal sealed class LjDeveloperMeasureCore
{
    #region 參數初始化

    private void Initialize(string contentsFolderPath)
    {
        var patternMatchParam = new LPatternMatchParam
        {
            Name = "[001]圖形搜尋",
            SearchRegion = new LRotatedRectangle(320, 307.4, 640, 614.8, 0),
            StartAngle = -40,
            EndAngle = 40,
            ScoreThreshold = 30,
            Timeout = 5,
            DetectCount = BallCount,
            FitDeformation = LPatternMatchFitDeformation.None,
        };
        patternMatchParam.PatternSetting.PatternRegion =
            new LCircle(_activeRefPatternX, _activeRefPatternY, BallSettings.PatternRadius);
        patternMatchParam.PatternSetting.EnableMaskMap = false;
        patternMatchParam.PatternSetting.ExtractionSetting = LPatternMatchEdgeExtractionSetting.Auto;
        patternMatchParam.PatternSetting.Sensitivity = 1;
        _patternMatch.Param = patternMatchParam;

        EnsureSphereRegionsAtRef();
        EnsureDatumPlaneTool();
    }

    private void EnsureDatumPlaneTool()
    {
        if (!BallSettings.DatumPlaneEnabled) return;

        var planeParam = _datumPlane.Param;
        planeParam.Name = "[000]滾輪基準面";
        planeParam.Region = new LRotatedRectangle(
            BallSettings.DatumPlaneCenterX,
            BallSettings.DatumPlaneCenterY,
            BallSettings.DatumPlaneWidth,
            BallSettings.DatumPlaneHeight,
            BallSettings.DatumPlaneAngle);
        planeParam.Method = LPlaneMethod.LeastSquare;
        planeParam.TargetData = LPlaneTargetData.SampledDataInAllRegions;
        _datumPlane.Param = planeParam;
    }

    private void EnsureSphereRegionsAtRef()
    {
        for (var i = 0; i < BallCount; i++)
        {
            var sphereParam = _spheres[i].Param;
            sphereParam.Name = $"[002]球_{i}";
            sphereParam.Region = new LCircle(_activeRefSphereX, _activeRefSphereY, BallSettings.SphereRadius);
            sphereParam.Method = LSphereMethod.LeastSquare;
            sphereParam.TargetData = LSphereTargetData.SampledDataInAllRegions;
            _spheres[i].Param = sphereParam;
        }
    }

    #endregion

    #region 設定/更新圖形訊息

    public void UpdatePattern(LHeightImage referenceHeightImage, LGrayImage referenceGrayImage)
    {
        AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} UpdatePattern（重訓圖形搜尋樣板）----");
        _patternMatch.UpdatePattern(referenceHeightImage);
    }

    #endregion

    #region 設定/更新參考值

    public void UpdateReference(LHeightImage referenceHeightImage, LGrayImage referenceGrayImage)
    {
        AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} UpdateReference（重新 baseline 球擬合）----");
        _patternMatch.UpdateReference(referenceHeightImage);
        if (BallSettings.DatumPlaneEnabled)
        {
            EnsureDatumPlaneTool();
            _datumPlane.UpdateReference(referenceHeightImage);
        }

        for (var i = 0; i < BallCount; i++)
            _spheres[i].UpdateReference(referenceHeightImage);
    }

    #endregion

    #region 參考中心

    public void ReloadReferenceFromConfig()
    {
        ApplyReferenceCenters(
            BallSettings.RefPatternX,
            BallSettings.RefPatternY,
            BallSettings.RefSphereX,
            BallSettings.RefSphereY);
        AppendLog(
            $"---- {DateTime.Now:HH:mm:ss.fff} ReloadReferenceFromConfig：Ref=({BallSettings.RefPatternX:F3}, {BallSettings.RefPatternY:F3}) ----");
    }

    public void SetReferenceCenters(double patternX, double patternY, double sphereX, double sphereY)
        => ApplyReferenceCenters(patternX, patternY, sphereX, sphereY);

    private void ApplyReferenceCenters(double patternX, double patternY, double sphereX, double sphereY)
    {
        _activeRefPatternX = patternX;
        _activeRefPatternY = patternY;
        _activeRefSphereX = sphereX;
        _activeRefSphereY = sphereY;

        var patternParam = _patternMatch.Param;
        patternParam.PatternSetting.PatternRegion =
            new LCircle(_activeRefPatternX, _activeRefPatternY, BallSettings.PatternRadius);
        _patternMatch.Param = patternParam;

        EnsureSphereRegionsAtRef();
    }

    #endregion

    #region 執行

    public IList<ILToolInfo> Execute(LHeightImage inputHeightImage, LGrayImage inputGrayImage)
    {
        var results = new List<ILToolInfo>();
        var unitDic = CreateUnitDictionary();

        var log = new StringBuilder();
        log.AppendLine($"========== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  Execute ==========");
        log.AppendLine(
            $"BallCount={BallCount}, Ref=({_activeRefPatternX:F3}, {_activeRefPatternY:F3}), " +
            $"Expected={BallSettings.ExpectedBallDiameter}mm ±{BallSettings.MaxDiameterError}");

        _patternMatch.Execute(inputHeightImage);

        var patternMatchToolInfo = CreatePatternMatchToolInfo(unitDic);
        var pmEach = patternMatchToolInfo.ToolMeasureValues().EachValues;
        var eachResults = _patternMatch.Result?.EachResults;
        var detectedCount = Math.Min(BallCount, eachResults?.Length ?? 0);

        log.AppendLine($"PatternMatch 候選 {detectedCount} 個:");
        for (var i = 0; i < detectedCount; i++)
        {
            var pos = eachResults[i].AbsolutePosition;
            var scoreText = GetPmScoreText(pmEach, i);
            log.AppendLine(
                $"  [{i}] score={scoreText}, X={pos.CenterPosition.X:F2}, Y={pos.CenterPosition.Y:F2}, θ={pos.Angle:F2}");
        }

        results.Add(patternMatchToolInfo);

        var sphereResultTypeList = CreateSphereResultTypeList();
        var sphereCandidates =
            new List<(int Index, double X, double Y, double Z, double D, double PmScore, string DeltaSrc, LSphereToolInfo Info)>();

        log.AppendLine("Sphere 擬合:");
        EnsureSphereRegionsAtRef();

        for (var i = 0; i < detectedCount; i++)
        {
            var pmScore = GetPmScore(pmEach, i);
            if (pmScore <= 0)
            {
                log.AppendLine($"  [{i}] PatternMatch 空槽位 (score=0)，跳過球擬合");
                continue;
            }

            if (!TryFitSphereCandidate(
                    i,
                    pmScore,
                    inputHeightImage,
                    eachResults,
                    sphereResultTypeList,
                    unitDic,
                    out var x,
                    out var y,
                    out var z,
                    out var d,
                    out var deltaSrc,
                    out var info,
                    out var fitLog))
            {
                log.AppendLine(fitLog);
                continue;
            }

            log.AppendLine(fitLog);
            sphereCandidates.Add((i, x, y, z, d, pmScore, deltaSrc, info));
        }

        sphereCandidates.Sort((a, b) =>
        {
            var da = Math.Abs(a.D - BallSettings.ExpectedBallDiameter);
            var db = Math.Abs(b.D - BallSettings.ExpectedBallDiameter);
            var cmp = da.CompareTo(db);
            return cmp != 0 ? cmp : b.PmScore.CompareTo(a.PmScore);
        });

        var minSepSq = BallSettings.MinSphereCenterSeparationMm * BallSettings.MinSphereCenterSeparationMm;
        var keptCenters = new List<(double X, double Y)>();
        var keepCount = 0;

        foreach (var c in sphereCandidates)
        {
            var isDuplicate = keptCenters.Any(k =>
            {
                var dx = c.X - k.X;
                var dy = c.Y - k.Y;
                return dx * dx + dy * dy < minSepSq;
            });

            if (isDuplicate)
            {
                log.AppendLine($"  [{c.Index}] X={c.X:F2}, Y={c.Y:F2} -> 丟棄(與已保留球中心距離 < {BallSettings.MinSphereCenterSeparationMm:F0}mm)");
                continue;
            }

            keptCenters.Add((c.X, c.Y));
            results.Add(c.Info);
            keepCount++;
            log.AppendLine($"  [{c.Index}] delta={c.DeltaSrc} -> 最終保留 (score={c.PmScore:F1})");
            if (keepCount >= BallCount)
                break;
        }

        log.AppendLine($"最終保留 {keepCount} 顆");
        log.AppendLine();
        AppendLog(log.ToString());

        return results;
    }

    private static LDeltaPosition CreateZeroDeltaAt(double centerX, double centerY)
        => new LDeltaPosition
        {
            Enable = true,
            X = 0,
            Y = 0,
            Theta = 0,
            Center = new LDPoint2D { X = centerX, Y = centerY },
        };

    /// <summary>
    /// 對單一 PM 候選嘗試多種 delta，選直徑最接近預期且可接受的結果。
    /// log 顯示尋球 D≈25 但 Result.DeltaPosition 常出現 D≈90，故 Zero@Ref 優先。
    /// </summary>
    private bool TryFitSphereCandidate(
        int candidateIndex,
        double pmScore,
        LHeightImage inputHeightImage,
        LEachPatternMatchResult[] eachResults,
        Dictionary<LSphereResultType, string> sphereResultTypeList,
        Dictionary<LUnitType, string> unitDic,
        out double x,
        out double y,
        out double z,
        out double d,
        out string deltaSrc,
        out LSphereToolInfo info,
        out string logLine)
    {
        x = y = z = d = 0;
        deltaSrc = "";
        info = null;
        logLine = "";

        var pmCenter = eachResults[candidateIndex].AbsolutePosition.CenterPosition;
        // 球心必須接近對應 PM 候選；若飄離太遠，多半是地面/錯配假球。
        var maxCenterDriftMm = Math.Max(BallSettings.ExpectedBallDiameter * 1.2, BallSettings.SphereRadius * 2.5);
        var maxCenterDriftSq = maxCenterDriftMm * maxCenterDriftMm;

        var deltas = BuildSphereDeltaCandidates(candidateIndex, eachResults);
        var bestAcceptableError = double.MaxValue;
        var bestAcceptable = (
            X: 0.0, Y: 0.0, Z: 0.0, D: 0.0, Src: "", Info: (LSphereToolInfo)null);

        var bestAnyError = double.MaxValue;
        var bestAny = bestAcceptable;

        foreach (var (delta, src) in deltas)
        {
            _spheres[candidateIndex].Execute(inputHeightImage, delta);
            var candidateInfo = new LSphereToolInfo(_spheres[candidateIndex], sphereResultTypeList, unitDic, 2 + candidateIndex);
            var sv = candidateInfo.ToolMeasureValues().Values;
            if (sv == null || sv.Length < 4) continue;

            var cx = sv[0].Value;
            var cy = sv[1].Value;
            var cz = sv[2].Value;
            var cd = sv[3].Value;
            if (double.IsNaN(cd) || double.IsInfinity(cd)) continue;

            var dx = cx - pmCenter.X;
            var dy = cy - pmCenter.Y;
            if (dx * dx + dy * dy > maxCenterDriftSq) continue;

            var err = Math.Abs(cd - BallSettings.ExpectedBallDiameter);
            if (err < bestAnyError)
            {
                bestAnyError = err;
                bestAny = (cx, cy, cz, cd, src, candidateInfo);
            }

            if (!BallSettings.IsAcceptableSphereDiameter(cd) || err >= bestAcceptableError) continue;

            bestAcceptableError = err;
            bestAcceptable = (cx, cy, cz, cd, src, candidateInfo);
        }

        if (bestAcceptable.Info != null)
        {
            x = bestAcceptable.X;
            y = bestAcceptable.Y;
            z = bestAcceptable.Z;
            d = bestAcceptable.D;
            deltaSrc = bestAcceptable.Src;
            info = bestAcceptable.Info;
            logLine =
                $"  [{candidateIndex}] delta={deltaSrc}, X={x:F2}, Y={y:F2}, Z={z:F2}, D={d:F3}, score={pmScore:F1} -> 候選保留";
            return true;
        }

        if (bestAny.Info != null)
        {
            logLine =
                $"  [{candidateIndex}] 全部 delta 皆不合格；最佳 delta={bestAny.Src}, X={bestAny.X:F2}, Y={bestAny.Y:F2}, Z={bestAny.Z:F2}, D={bestAny.D:F3} -> 丟棄(超過 MaxDiameterError)";
            return false;
        }

        logLine = $"  [{candidateIndex}] 無法取得球擬合結果";
        return false;
    }

    private List<(LDeltaPosition Delta, string Source)> BuildSphereDeltaCandidates(
        int candidateIndex,
        LEachPatternMatchResult[] eachResults)
    {
        var deltas = new List<(LDeltaPosition, string)>();
        var seen = new HashSet<string>();

        void Add(LDeltaPosition? delta, string source)
        {
            if (!delta.HasValue) return;
            var d = delta.Value;
            var key = $"{d.X:F3},{d.Y:F3},{d.Theta:F3},{d.Center.X:F3},{d.Center.Y:F3}";
            if (!seen.Add(key)) return;
            deltas.Add((d, source));
        }

        Add(CreateZeroDeltaAt(_activeRefPatternX, _activeRefPatternY), "Zero@Ref");

        if (candidateIndex == 0 && _patternMatch.Result != null)
            Add(_patternMatch.Result.DeltaPosition, "Result.DeltaPosition");

        if (eachResults != null && candidateIndex < eachResults.Length)
        {
            var rel = eachResults[candidateIndex].RegionRelatedPosition;
            Add(
                new LDeltaPosition
                {
                    Enable = true,
                    X = rel.CenterPosition.X,
                    Y = rel.CenterPosition.Y,
                    Theta = rel.Angle,
                    Center = new LDPoint2D { X = _activeRefPatternX, Y = _activeRefPatternY },
                },
                $"RegionRelated[{candidateIndex}]");

            var abs = eachResults[candidateIndex].AbsolutePosition;
            Add(
                new LDeltaPosition
                {
                    Enable = true,
                    X = abs.CenterPosition.X - _activeRefPatternX,
                    Y = abs.CenterPosition.Y - _activeRefPatternY,
                    Theta = abs.Angle,
                    Center = new LDPoint2D { X = _activeRefPatternX, Y = _activeRefPatternY },
                },
                $"Each[{candidateIndex}]→Ref");
        }

        return deltas;
    }

    #endregion

    #region 基準面（滾輪）

    /// <summary>量測滾輪／治具基準面（LPlaneTool）。需先 UpdateReference。</summary>
    public DatumPlaneMeasureResult TryMeasureDatumPlane(
        LHeightImage inputHeightImage,
        double? firstBallX = null,
        double? firstBallY = null,
        double? firstBallZ = null)
    {
        if (!BallSettings.DatumPlaneEnabled)
        {
            return new DatumPlaneMeasureResult
            {
                Measured = false,
                Ok = false,
                ErrorMessage = "DatumPlane 未啟用（ScannerConfig.DatumPlaneEnabled=false）。",
            };
        }

        EnsureDatumPlaneTool();
        AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} 基準面量測 ROI=({BallSettings.DatumPlaneCenterX:F1},{BallSettings.DatumPlaneCenterY:F1}) ----");

        var rc = _datumPlane.Execute(inputHeightImage);
        if (rc != LErrorCode.Success)
        {
            var msg = $"基準面 Execute 失敗：{rc}";
            AppendLog(msg);
            return new DatumPlaneMeasureResult { Measured = true, Ok = false, ErrorMessage = msg };
        }

        var planeResult = _datumPlane.Result;
        if (planeResult is null)
        {
            const string msg = "基準面 Execute 成功但 Result 為 null。";
            AppendLog(msg);
            return new DatumPlaneMeasureResult { Measured = true, Ok = false, ErrorMessage = msg };
        }

        var a = LPlaneResultExtensions.GetResultValue(planeResult, LPlaneResultType.PlaneA);
        var b = LPlaneResultExtensions.GetResultValue(planeResult, LPlaneResultType.PlaneB);
        var c = LPlaneResultExtensions.GetResultValue(planeResult, LPlaneResultType.PlaneC);

        var cx = BallSettings.DatumPlaneCenterX;
        var cy = BallSettings.DatumPlaneCenterY;
        var planeZAtCenter = TryPlaneZAt(a, b, c, cx, cy);
        double? ballRelativeZ = null;
        if (firstBallX is double bx && firstBallY is double by && firstBallZ is double bz
            && TryPlaneZAt(a, b, c, bx, by) is double planeZAtBall)
        {
            ballRelativeZ = bz - planeZAtBall;
        }

        var info = new LPlaneToolInfo(_datumPlane, CreatePlaneResultTypeList(), CreateUnitDictionary(), 0);
        AppendLog($"  平面 A={a:F6} B={b:F6} C={c:F6} Z@ROI=({FormatNullable(planeZAtCenter)})"
                  + (ballRelativeZ is double rel ? $" 球相對高度={rel:F3}" : ""));

        return new DatumPlaneMeasureResult
        {
            Measured = true,
            Ok = true,
            PlaneA = a,
            PlaneB = b,
            PlaneC = c,
            PlaneZAtRegionCenter = planeZAtCenter,
            FirstBallRelativeZ = ballRelativeZ,
        };
    }

    private static double? TryPlaneZAt(double a, double b, double c, double x, double y)
    {
        if (Math.Abs(c) < 1e-9) return null;
        return -(a * x + b * y) / c;
    }

    private static string FormatNullable(double? v) => v is double d ? d.ToString("F3") : "n/a";

    #endregion

    #region 校正驗證

    /// <summary>
    /// 校正前尋球：在 PM 候選與 Ref 附近小範圍格點，找出直徑最接近預期值的球心。
    /// </summary>
    public bool TryDiscoverCalibrationBallCenter(
        LHeightImage inputHeightImage,
        out double centerX,
        out double centerY,
        out double diameter,
        out string message)
    {
        centerX = _activeRefPatternX;
        centerY = _activeRefPatternY;
        diameter = double.NaN;
        message = "";

        AppendLog(
            $"---- {DateTime.Now:HH:mm:ss.fff} 校正尋球開始：seed Ref=({_activeRefPatternX:F3}, {_activeRefPatternY:F3}) ----");

        _patternMatch.Execute(inputHeightImage);
        var eachResults = _patternMatch.Result?.EachResults;
        if (eachResults == null || eachResults.Length == 0)
        {
            message = "PatternMatch 無候選，請確認球在視野內。";
            return false;
        }

        var unitDic = CreateUnitDictionary();
        var pmInfo = CreatePatternMatchToolInfo(unitDic);
        var pmEach = pmInfo.ToolMeasureValues().EachValues;
        var candidates = new List<(double X, double Y, string Source)>
        {
            (_activeRefPatternX, _activeRefPatternY, "Ref")
        };

        // 手放球容許較大偏移；仍以直徑門檻過濾假球，並設絕對上限避免全視野掃描。
        var gridStep = BallSettings.CalibrationDiscoverGridStepMm;
        var gridRadius = Math.Min(
            BallSettings.CalibrationDiscoverAbsoluteMaxMm,
            BallSettings.CalibrationDiscoverSearchRadiusMm);

        var detectedCount = Math.Min(BallCount, eachResults.Length);
        for (var i = 0; i < detectedCount; i++)
        {
            var score = GetPmScore(pmEach, i);
            if (score <= 0) continue;

            var p = eachResults[i].AbsolutePosition.CenterPosition;
            var dx = p.X - _activeRefPatternX;
            var dy = p.Y - _activeRefPatternY;
            if (dx * dx + dy * dy > gridRadius * gridRadius) continue;
            candidates.Add((p.X, p.Y, $"PM[{i}] score={score:F1}"));
        }

        AppendLog(
            $"---- {DateTime.Now:HH:mm:ss.fff} 校正尋球：候選 {candidates.Count} 點 + 網格 R={gridRadius:F0}mm step={gridStep:F1}mm ----");

        for (var dx = -gridRadius; dx <= gridRadius; dx += gridStep)
        {
            for (var dy = -gridRadius; dy <= gridRadius; dy += gridStep)
            {
                if (dx * dx + dy * dy > gridRadius * gridRadius) continue;
                candidates.Add((_activeRefPatternX + dx, _activeRefPatternY + dy, $"Grid({dx:+0;-0;0},{dy:+0;-0;0})"));
            }
        }

        var originalPatternX = _activeRefPatternX;
        var originalPatternY = _activeRefPatternY;
        var originalSphereX = _activeRefSphereX;
        var originalSphereY = _activeRefSphereY;

        var bestFound = false;
        var bestError = double.MaxValue;
        var bestX = centerX;
        var bestY = centerY;
        var bestD = diameter;
        var bestSource = "";
        var bestAnyError = double.MaxValue;
        var bestAnyD = double.NaN;
        var bestAnyX = double.NaN;
        var bestAnyY = double.NaN;
        var bestAnySource = "";

        foreach (var c in candidates)
        {
            if (!TryProbeSphereAt(inputHeightImage, c.X, c.Y, out var px, out var py, out var pd))
                continue;

            var err = Math.Abs(pd - BallSettings.ExpectedBallDiameter);
            if (err < bestAnyError)
            {
                bestAnyError = err;
                bestAnyD = pd;
                bestAnyX = px;
                bestAnyY = py;
                bestAnySource = c.Source;
            }
            if (err > BallSettings.MaxDiameterError) continue;

            if (err < bestError)
            {
                bestFound = true;
                bestError = err;
                bestX = px;
                bestY = py;
                bestD = pd;
                bestSource = c.Source;
            }
        }

        if (!bestFound)
        {
            ApplyReferenceCenters(originalPatternX, originalPatternY, originalSphereX, originalSphereY);
            message = $"未找到直徑 {BallSettings.ExpectedBallDiameter}±{BallSettings.MaxDiameterError} mm 的校正球。";
            if (!double.IsNaN(bestAnyD))
            {
                message += $" 最接近候選：({bestAnyX:F2}, {bestAnyY:F2}), D={bestAnyD:F3}, 來源={bestAnySource}。";
            }
            AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} 校正尋球失敗：{message} ----");
            return false;
        }

        ApplyReferenceCenters(bestX, bestY, bestX, bestY);
        centerX = bestX;
        centerY = bestY;
        diameter = bestD;
        message = $"校正尋球成功：({bestX:F2}, {bestY:F2})，D={bestD:F3} mm，來源={bestSource}";
        AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} {message} ----");
        return true;
    }

    private bool TryProbeSphereAt(
        LHeightImage inputHeightImage,
        double regionCenterX,
        double regionCenterY,
        out double fittedX,
        out double fittedY,
        out double fittedD)
    {
        fittedX = fittedY = fittedD = double.NaN;

        // 用臨時 SphereTool + 當前影像更新 reference，避免舊 baseline 影響校正尋球。
        var probe = new LSphereTool();
        var probeParam = probe.Param;
        probeParam.Name = "[CAL]ProbeSphere";
        probeParam.Region = new LCircle(regionCenterX, regionCenterY, BallSettings.SphereRadius);
        probeParam.Method = LSphereMethod.LeastSquare;
        probeParam.TargetData = LSphereTargetData.SampledDataInAllRegions;
        probe.Param = probeParam;

        probe.UpdateReference(inputHeightImage);
        probe.Execute(
            inputHeightImage,
            new LDeltaPosition
            {
                Enable = true,
                X = 0,
                Y = 0,
                Theta = 0,
                Center = new LDPoint2D { X = regionCenterX, Y = regionCenterY },
            });

        var info = new LSphereToolInfo(probe, CreateSphereResultTypeList(), CreateUnitDictionary(), 2);
        var values = info.ToolMeasureValues().Values;
        if (values == null || values.Length < 4) return false;

        fittedX = values[0].Value;
        fittedY = values[1].Value;
        fittedD = values[3].Value;
        return !(double.IsNaN(fittedD) || double.IsInfinity(fittedD));
    }

    /// <summary>
    /// 校正後驗證：在已 UpdateReference 的 _spheres[0] 上以 Zero@Ref 直接探測（與尋球探測一致）。
    /// </summary>
    public bool TryValidateCalibrationProbe(
        LHeightImage inputHeightImage,
        out double centerX,
        out double centerY,
        out double centerZ,
        out double diameter,
        out string message)
    {
        centerX = centerY = centerZ = diameter = 0;
        EnsureSphereRegionsAtRef();
        _spheres[0].Execute(inputHeightImage, CreateZeroDeltaAt(_activeRefPatternX, _activeRefPatternY));

        var info = new LSphereToolInfo(_spheres[0], CreateSphereResultTypeList(), CreateUnitDictionary(), 2);
        var values = info.ToolMeasureValues().Values;
        if (values == null || values.Length < 4)
        {
            message = "球擬合驗證失敗：無法讀取量測值。";
            return false;
        }

        centerX = values[0].Value;
        centerY = values[1].Value;
        centerZ = values[2].Value;
        diameter = values[3].Value;

        if (!BallSettings.IsAcceptableSphereDiameter(diameter))
        {
            message =
                $"球擬合驗證失敗：Zero@Ref 量得 D={diameter:F3} mm（預期 {BallSettings.ExpectedBallDiameter}±{BallSettings.MaxDiameterError}）。";
            AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} {message} ----");
            return false;
        }

        message =
            $"球擬合驗證通過：({centerX:F2}, {centerY:F2})，Z={centerZ:F2}，D={diameter:F3} mm（Zero@Ref）。";
        AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} {message} ----");
        return true;
    }

    /// <summary>校正後驗證：Execute 至少保留一顆直徑合理的球。</summary>
    public bool TryValidateCalibrationExecute(IList<ILToolInfo> results, out string message)
    {
        if (TryGetFirstKeptSphere(results, out var x, out var y, out var z, out var d, out var count))
        {
            message =
                $"球擬合驗證通過：保留 {count} 顆，代表球心 ({x:F2}, {y:F2}, {z:F2})，D={d:F3} mm。";
            return true;
        }

        message =
            $"球擬合驗證失敗：無直徑 {BallSettings.ExpectedBallDiameter}±{BallSettings.MaxDiameterError} mm 的球。"
            + "請確認 Scan 參數、球在視野內，且 RefPattern 接近球心。";
        return false;
    }

    /// <summary>取第一顆保留球的量測值（用於校正後寫回 Ref）。</summary>
    public bool TryGetFirstKeptSphere(
        IList<ILToolInfo> results,
        out double centerX,
        out double centerY,
        out double centerZ,
        out double diameter,
        out int keptCount)
    {
        centerX = centerY = centerZ = diameter = 0;
        keptCount = 0;

        foreach (var result in results)
        {
            if (!(result is LSphereToolInfo sphereInfo)) continue;

            var values = sphereInfo.ToolMeasureValues().Values;
            if (values == null || values.Length < 4) continue;

            var d = values[3].Value;
            if (!BallSettings.IsAcceptableSphereDiameter(d)) continue;

            keptCount++;
            if (keptCount == 1)
            {
                centerX = values[0].Value;
                centerY = values[1].Value;
                centerZ = values[2].Value;
                diameter = d;
            }
        }

        return keptCount >= 1;
    }

    #endregion

    #region Functions

    public double ActiveRefPatternX => _activeRefPatternX;
    public double ActiveRefPatternY => _activeRefPatternY;
    public double ActiveRefSphereX => _activeRefSphereX;
    public double ActiveRefSphereY => _activeRefSphereY;

    public string[] GetDisplayText(IList<ILToolInfo> results)
        => results.Select(result => result.DisplayText()).ToArray();

    public ILToolMeasureValues[] GetMeasureValues(IList<ILToolInfo> results)
        => results.Select(result => result.ToolMeasureValues()).ToArray();

    public void LogScannerEvent(string text) => AppendLog(text);

    private static Dictionary<LUnitType, string> CreateUnitDictionary()
        => new Dictionary<LUnitType, string>
        {
            { LUnitType.None, "" },
            { LUnitType.Mm, "mm" },
            { LUnitType.Degree, "°" },
            { LUnitType.Mm2, "mm²" },
            { LUnitType.Mm3, "mm³" },
            { LUnitType.Number, "個" },
            { LUnitType.Percent, "%" },
        };

    private static Dictionary<LSphereResultType, string> CreateSphereResultTypeList()
        => new Dictionary<LSphereResultType, string>
        {
            { LSphereResultType.DetectSphereSphereCenterX, "中心X" },
            { LSphereResultType.DetectSphereSphereCenterY, "中心Y" },
            { LSphereResultType.DetectSphereSphereCenterZ, "中心Z" },
            { LSphereResultType.DetectSphereSphereDiameter, "直徑" },
        };

    private static Dictionary<LPlaneResultType, string> CreatePlaneResultTypeList()
        => new Dictionary<LPlaneResultType, string>
        {
            { LPlaneResultType.PlaneA, "平面A" },
            { LPlaneResultType.PlaneB, "平面B" },
            { LPlaneResultType.PlaneC, "平面C" },
        };

    private LPatternMatchToolInfo CreatePatternMatchToolInfo(Dictionary<LUnitType, string> unitDic)
    {
        var patternResultTypeList = new Dictionary<LPatternMatchResultType, string>();
        var eachPatternResultTypeList = new Dictionary<LEachPatternMatchResultType, string>
        {
            { LEachPatternMatchResultType.EachPatternMatchResultScore, "相關值" },
        };
        return new LPatternMatchToolInfo(_patternMatch, patternResultTypeList, eachPatternResultTypeList, unitDic, 1);
    }

    private static double GetPmScore(ILEachMeasureValue[] pmEach, int index)
    {
        if (pmEach == null || index >= pmEach.Length || pmEach[index].Values.Length == 0)
            return double.NaN;
        return pmEach[index].Values[0].Value;
    }

    private static string GetPmScoreText(ILEachMeasureValue[] pmEach, int index)
    {
        var score = GetPmScore(pmEach, index);
        return double.IsNaN(score) ? "?" : score.ToString("F1");
    }

    #endregion

    #region 建構函數

    private readonly LHeightImage _referenceHeightImage;
    private readonly LGrayImage _referenceGrayImage;
    private readonly string _logFile;

    public LjDeveloperMeasureCore(string contentsFolderPath)
    {
        _activeRefPatternX = RefPatternX;
        _activeRefPatternY = RefPatternY;
        _activeRefSphereX = RefSphereX;
        _activeRefSphereY = RefSphereY;

        _spheres = Enumerable.Range(0, BallCount).Select(_ => new LSphereTool()).ToArray();

        var rootFolder = Path.GetDirectoryName(contentsFolderPath.TrimEnd(Path.DirectorySeparatorChar));
        var logDir = Path.Combine(rootFolder ?? contentsFolderPath, "logs");
        try { Directory.CreateDirectory(logDir); } catch { /* ignore */ }
        _logFile = Path.Combine(logDir, $"measure_{DateTime.Now:yyyyMMdd}.log");

        AppendLog($"###### {DateTime.Now:yyyy-MM-dd HH:mm:ss} ljd_measure 啟動 ######");
        AppendLog($"掃描參數: {BallSettings.ScannerProgramDescription}, ref 目錄: {contentsFolderPath}");

        var heightImage = new LHeightImage();
        heightImage.Read(Path.Combine(contentsFolderPath, "ref_height.png"));
        _referenceHeightImage = heightImage;

        var grayImage = new LGrayImage();
        grayImage.Read(Path.Combine(contentsFolderPath, "ref_gray.png"));
        _referenceGrayImage = grayImage;

        ReloadReferenceFromConfig();
        Initialize(contentsFolderPath);
        UpdatePattern(_referenceHeightImage, _referenceGrayImage);
        UpdateReference(_referenceHeightImage, _referenceGrayImage);
    }

    private void AppendLog(string text)
    {
        try { File.AppendAllText(_logFile, text + Environment.NewLine, Encoding.UTF8); }
        catch { /* ignore */ }
    }

    #endregion

    #region 實例

    private readonly int BallCount = BallSettings.BallCount;
    private readonly double RefPatternX = BallSettings.RefPatternX;
    private readonly double RefPatternY = BallSettings.RefPatternY;
    private readonly double RefSphereX = BallSettings.RefSphereX;
    private readonly double RefSphereY = BallSettings.RefSphereY;

    private double _activeRefPatternX;
    private double _activeRefPatternY;
    private double _activeRefSphereX;
    private double _activeRefSphereY;

    private readonly LPatternMatchTool _patternMatch = new LPatternMatchTool();
    private readonly LPlaneTool _datumPlane = new();
    private readonly LSphereTool[] _spheres;

    #endregion
}
