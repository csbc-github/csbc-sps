namespace ThreeDScan.Infrastructure.Measure;

using Keyence.LjDevCommon;
using Keyence.LjDevMeasure;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

// PM 與視埠工具沿用 LJ Developer；實際球心／直徑由原始高度取樣的穩健球面擬合取得。

/// <summary>LJ Developer 尋球／平面量測核心（對齊 ljd_measure）。</summary>
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
            // 重複角度與治具假候選會佔用排名；先抓滿候選，再去重及驗證球面。
            DetectCount = PatternDetectCount,
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
        for (var i = 0; i < _spheres.Length; i++)
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

    /// <summary>用目前高度／輝度重訓圖形搜尋樣板（區域已是 ActiveRef）。</summary>
    public void UpdatePattern(LHeightImage referenceHeightImage, LGrayImage referenceGrayImage)
    {
        AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} UpdatePattern（重訓圖形搜尋樣板）----");
        _patternMatch.UpdatePattern(referenceHeightImage);
    }

    #endregion

    #region 設定/更新參考值

    /// <summary>重算 Pattern／Sphere／基準面的 baseline，供後續 Execute／Zero@Ref 探測。</summary>
    public void UpdateReference(LHeightImage referenceHeightImage, LGrayImage referenceGrayImage)
    {
        AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} UpdateReference（重新 baseline 球擬合）----");
        _patternMatch.UpdateReference(referenceHeightImage);
        if (BallSettings.DatumPlaneEnabled)
        {
            EnsureDatumPlaneTool();
            _datumPlane.UpdateReference(referenceHeightImage);
        }

        for (var i = 0; i < _spheres.Length; i++)
            _spheres[i].UpdateReference(referenceHeightImage);
    }

    #endregion

    #region 參考中心

    /// <summary>從 BallSettings（即 ScannerConfig）載入 Ref 種子到樣板 ROI 與球擬合 ROI。</summary>
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

    /// <summary>把目前使用的 Pattern／Sphere 中心改成指定座標（尋球成功後用來鎖定新球心）。</summary>
    public void SetReferenceCenters(double patternX, double patternY, double sphereX, double sphereY)
    {
        ApplyReferenceCenters(patternX, patternY, sphereX, sphereY);
    }

    /// <summary>同步 _activeRef* 與 SDK 工具 ROI；不寫資料庫。</summary>
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

    /// <summary>
    /// 儀器校正量測：PatternMatch 找候選，以原始高度取樣擬合並檢查外圈；按 PM 分數順序取 BallCount 顆。
    /// </summary>
    public IList<ILToolInfo> Execute(
        LHeightImage inputHeightImage,
        LGrayImage inputGrayImage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _keptSphereMeasurements.Clear();
        var frame = HeightImageFrameAdapter.FromLHeightImage(inputHeightImage);
        var results = new List<ILToolInfo>();
        var unitDic = CreateUnitDictionary();

        var log = new StringBuilder();
        log.AppendLine($"========== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  Execute ==========");
        log.AppendLine(
            $"BallCount={BallCount}, Ref=({_activeRefPatternX:F3}, {_activeRefPatternY:F3}), " +
            $"Expected={BallSettings.ExpectedBallDiameter}mm ±{BallSettings.MaxDiameterError}");
        log.AppendLine("校正球心採標準尺寸球面及獨立取樣驗證；自由估徑保留實測診斷，SDK 3D 球圖示僅供輔助檢視。");

        _patternMatch.Execute(inputHeightImage);

        var patternMatchToolInfo = CreatePatternMatchToolInfo(unitDic);
        var pmEach = patternMatchToolInfo.ToolMeasureValues().EachValues;
        var eachResults = _patternMatch.Result?.EachResults ?? [];
        var rawPmCount = Math.Min(PatternDetectCount, eachResults.Length);

        log.AppendLine($"PatternMatch 原始候選 {rawPmCount} 個 (DetectCount={PatternDetectCount}):");
        for (var i = 0; i < rawPmCount; i++)
        {
            var pos = eachResults[i].AbsolutePosition;
            var scoreText = GetPmScoreText(pmEach, i);
            log.AppendLine(
                $"  [{i}] score={scoreText}, X={pos.CenterPosition.X:F2}, Y={pos.CenterPosition.Y:F2}, θ={pos.Angle:F2}");
        }

        var uniquePmIndices = SelectUniquePatternMatchIndices(eachResults, pmEach, rawPmCount);
        log.AppendLine(
            $"PatternMatch 位置去重後 {uniquePmIndices.Count} 個: [{string.Join(", ", uniquePmIndices)}]");

        results.Add(patternMatchToolInfo);

        var sphereResultTypeList = CreateSphereResultTypeList();
        var sphereCandidates =
            new List<(int Index, double X, double Y, double Z, double D, double PmScore, string DeltaSrc, LSphereToolInfo Info, RobustSphereFitResult Fit)>();

        log.AppendLine("Sphere 擬合:");
        EnsureSphereRegionsAtRef();

        foreach (var i in uniquePmIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                    frame,
                    eachResults,
                    sphereResultTypeList,
                    unitDic,
                    out var x,
                    out var y,
                    out var z,
                    out var d,
                    out var deltaSrc,
                    out var info,
                    out var fitLog,
                    out var fit,
                    cancellationToken))
            {
                log.AppendLine(fitLog);
                continue;
            }

            log.AppendLine(fitLog);
            sphereCandidates.Add((i, x, y, z, d, pmScore, deltaSrc, info, fit));
        }

        var minSepSq = BallSettings.MinSphereCenterSeparationMm * BallSettings.MinSphereCenterSeparationMm;
        var keptCenters = new List<(double X, double Y)>();
        var keepCount = 0;

        // 維持 PM 分數順序；校正資格依球面模型驗證，直徑仍保留獨立診斷。
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
            _keptSphereMeasurements.Add(c.Info, c.Fit);
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
    {
        return new LDeltaPosition
        {
            Enable = true,
            X = 0,
            Y = 0,
            Theta = 0,
            Center = new LDPoint2D { X = centerX, Y = centerY },
        };
    }

    /// <summary>
    /// 對單一 PM 候選做原始高度球面擬合；SDK 工具只保留視埠顯示。
    /// 標準尺寸球心模型與獨立外圈須通過；自由估徑另作診斷。
    /// </summary>
    private bool TryFitSphereCandidate(
        int candidateIndex,
        double pmScore,
        LHeightImage inputHeightImage,
        HeightFrame frame,
        LEachPatternMatchResult[] eachResults,
        Dictionary<LSphereResultType, string> sphereResultTypeList,
        Dictionary<LUnitType, string> unitDic,
        out double x,
        out double y,
        out double z,
        out double d,
        out string deltaSrc,
        out LSphereToolInfo info,
        out string logLine,
        out RobustSphereFitResult fitResult,
        CancellationToken cancellationToken = default)
    {
        x = y = z = d = 0;
        deltaSrc = "RawSphere@PM";
        info = null!;
        logLine = "";
        fitResult = null!;

        var pmCenter = eachResults[candidateIndex].AbsolutePosition.CenterPosition;
        var pmX = pmCenter.X;
        var pmY = pmCenter.Y;
        var sphereParam = _spheres[candidateIndex].Param;
        sphereParam.Name = $"[002]球_{candidateIndex}（SDK輔助圖示）";
        sphereParam.Region = new LCircle(pmX, pmY, BallSettings.SphereRadius);
        sphereParam.Method = LSphereMethod.LeastSquare;
        sphereParam.TargetData = LSphereTargetData.SampledDataInAllRegions;
        _spheres[candidateIndex].Param = sphereParam;
        _spheres[candidateIndex].UpdateReference(inputHeightImage);
        _spheres[candidateIndex].Execute(inputHeightImage, CreateZeroDeltaAt(pmX, pmY));

        var candidateInfo = new LSphereToolInfo(
            _spheres[candidateIndex], sphereResultTypeList, unitDic, 2 + candidateIndex);
        var sv = candidateInfo.ToolMeasureValues().Values;
        double sdkDiameter = sv is { Length: >= 4 } ? sv[3].Value : double.NaN;
        var fit = RobustSphereFitter.FitCalibrationSphere(
            frame, pmX, pmY, BallSettings.ExpectedBallDiameter, BallSettings.MaxDiameterError, cancellationToken);
        fitResult = fit;
        if (!IsAcceptableRawSphere(fit, pmX, pmY, out var reason))
        {
            logLine = $"  [{candidateIndex}] PM=({pmX:F2}, {pmY:F2}), score={pmScore:F1}, " +
                $"SDK D={sdkDiameter:F3} -> 丟棄(raw sphere: {reason}; {DescribeRawSphere(fit)})";
            return false;
        }

        x = fit.X;
        y = fit.Y;
        z = fit.Z;
        d = fit.DiameterMm;
        deltaSrc = fit.UsesKnownRadius ? "KnownRadius@PM" : "RawSphere@PM";
        info = candidateInfo;
        // SDK Result is a managed copy: assigning its Sphere does not change native Values().
        // Preserve LSphereToolInfo for Lj3DView, and override its displayed numbers only.
        UpdateSphereDisplay(candidateInfo, fit);
        logLine =
            $"  [{candidateIndex}] {deltaSrc} X={x:F2}, Y={y:F2}, Z={z:F2}, freeD={d:F3}, " +
            $"score={pmScore:F1}, SDK D={sdkDiameter:F3}, {DescribeRawSphere(fit)} -> 候選保留";
        return true;
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

    private static string FormatNullable(double? v)
    {
        return v is double d ? d.ToString("F3") : "n/a";
    }

    #endregion

    #region 校正驗證

    /// <summary>
    /// 在 ScannerConfig 指定的 Ref XY 做一次球擬合。
    /// 不搜尋附近、不用 Pattern Match、不與其他點比直徑；寫回的是該點的擬合球心。
    /// </summary>
    public bool TryFitCalibrationBallAtRef(
        LHeightImage inputHeightImage,
        out double centerX,
        out double centerY,
        out double diameter,
        out string message,
        CancellationToken cancellationToken = default)
    {
        var seedX = _activeRefPatternX;
        var seedY = _activeRefPatternY;
        centerX = seedX;
        centerY = seedY;
        diameter = double.NaN;
        message = "";

        AppendLog(
            $"---- {DateTime.Now:HH:mm:ss.fff} Ref 擬合開始：指定座標=({seedX:F3}, {seedY:F3}) ----");

        if (!TryProbeSphereAt(
                inputHeightImage, seedX, seedY,
                out var fittedX, out var fittedY, out _, out var fittedD, out var detail, cancellationToken))
        {
            message = $"球擬合失敗：指定 Ref 座標 ({seedX:F3}, {seedY:F3}) 的原始球面量測未通過：{detail}";
            AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} {message} ----");
            return false;
        }

        if (!BallSettings.IsAcceptableSphereDiameter(fittedD))
        {
            message =
                $"球擬合失敗：指定 Ref 座標量得 D={fittedD:F3} mm" +
                $"（預期 {BallSettings.ExpectedBallDiameter}±{BallSettings.MaxDiameterError}）。";
            AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} {message} ----");
            return false;
        }

        ApplyReferenceCenters(fittedX, fittedY, fittedX, fittedY);
        centerX = fittedX;
        centerY = fittedY;
        diameter = fittedD;
        message =
            $"Ref 擬合成功：擬合球心=({fittedX:F2}, {fittedY:F2})，D={fittedD:F3} mm" +
            $"（指定座標 ({seedX:F3}, {seedY:F3})）。";
        AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} {message} ----");
        return true;
    }

    /// <summary>
    /// 在指定 Ref 座標做與 Execute 相同的原始高度球面擬合及外圈驗證。
    /// </summary>
    private bool TryProbeSphereAt(
        LHeightImage inputHeightImage,
        double regionCenterX,
        double regionCenterY,
        out double fittedX,
        out double fittedY,
        out double fittedZ,
        out double fittedD,
        out string detail,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        fittedX = fittedY = fittedZ = fittedD = double.NaN;
        var frame = HeightImageFrameAdapter.FromLHeightImage(inputHeightImage);
        var fit = RobustSphereFitter.Fit(
            frame, regionCenterX, regionCenterY,
            BallSettings.ExpectedBallDiameter, BallSettings.MaxDiameterError, cancellationToken);
        bool acceptable = IsAcceptableRawSphere(fit, regionCenterX, regionCenterY, out var reason);
        detail = acceptable ? DescribeRawSphere(fit) : reason + "; " + DescribeRawSphere(fit);
        AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} RawSphere probe " +
            $"seed=({regionCenterX:F3},{regionCenterY:F3}), accepted={acceptable}, {detail} ----");
        if (!acceptable) return false;
        fittedX = fit.X;
        fittedY = fit.Y;
        fittedZ = fit.Z;
        fittedD = fit.DiameterMm;
        return true;
    }

    /// <summary>
    /// 校正後驗證：以相同原始高度擬合與外圈品質檢查，避免回頭採用 SDK 球徑。
    /// </summary>
    public bool TryValidateCalibrationProbe(
        LHeightImage inputHeightImage,
        out double centerX,
        out double centerY,
        out double centerZ,
        out double diameter,
        out string message,
        CancellationToken cancellationToken = default)
    {
        centerX = centerY = centerZ = diameter = 0;
        if (!TryProbeSphereAt(
                inputHeightImage, _activeRefPatternX, _activeRefPatternY,
                out centerX, out centerY, out centerZ, out diameter, out var detail, cancellationToken))
        {
            message = "球擬合驗證失敗：" + detail;
            return false;
        }

        message =
            $"球擬合驗證通過：({centerX:F2}, {centerY:F2})，Z={centerZ:F2}，D={diameter:F3} mm（原始球面與外圈驗證）。";
        AppendLog($"---- {DateTime.Now:HH:mm:ss.fff} {message} ----");
        return true;
    }

    /// <summary>校正後驗證：Execute 至少保留一顆通過球心模型品質的球。</summary>
    public bool TryValidateCalibrationExecute(IList<ILToolInfo> results, out string message)
    {
        if (TryGetFirstKeptSphere(results, out var x, out var y, out var z, out var d, out var count))
        {
            message =
                $"球擬合驗證通過：保留 {count} 顆，代表球心 ({x:F2}, {y:F2}, {z:F2})，自由估徑={d:F3} mm。";
            return true;
        }

        message =
            $"球擬合驗證失敗：無通過標準尺寸 {BallSettings.ExpectedBallDiameter} mm 球心模型及獨立取樣驗證的球。"
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
            if (!TryReadKeptSphere(result, out var x, out var y, out var z, out var d)) continue;

            keptCount++;
            if (keptCount == 1)
            {
                centerX = x;
                centerY = y;
                centerZ = z;
                diameter = d;
            }
        }

        return keptCount >= 1;
    }

    /// <summary>
    /// 讀本次 Execute 最終保留的原始球面量測；SDK ToolMeasureValues 僅供工具繪圖，不作 API 數值來源。
    /// </summary>
    public bool TryReadKeptSphere(
        ILToolInfo info, out double x, out double y, out double z, out double diameter)
    {
        x = y = z = diameter = 0;
        if (!_keptSphereMeasurements.TryGetValue(info, out var measured)) return false;
        x = measured.X;
        y = measured.Y;
        z = measured.Z;
        diameter = measured.DiameterMm;
        return true;
    }

    /// <summary>讀取校正球心模型及未改寫的自由估徑診斷。</summary>
    public bool TryReadKeptSphereFit(ILToolInfo info, out RobustSphereFitResult fit)
    {
        return _keptSphereMeasurements.TryGetValue(info, out fit!);
    }

    private static bool IsAcceptableRawSphere(
        RobustSphereFitResult fit, double seedX, double seedY, out string reason)
    {
        if (!fit.Success)
        {
            reason = fit.FailureReason ?? "原始球面擬合或外圈驗證失敗";
            return false;
        }
        if (!double.IsFinite(fit.X) || !double.IsFinite(fit.Y) || !double.IsFinite(fit.Z)
            || !double.IsFinite(fit.DiameterMm))
        {
            reason = "原始球面量測包含非有限數值";
            return false;
        }
        double dx = fit.X - seedX, dy = fit.Y - seedY;
        if (dx * dx + dy * dy > 25)
        {
            reason = "原始擬合球心偏離 PM／Ref 超過 5 mm";
            return false;
        }
        // Reference/probe measurements retain their free-diameter gate. A validated
        // standard-ball centre keeps the independent free diameter as a diagnostic.
        if (!fit.UsesKnownRadius && !BallSettings.IsAcceptableSphereDiameter(fit.DiameterMm))
        {
            reason = "超過 MaxDiameterError";
            return false;
        }
        reason = "";
        return true;
    }

    private static string DescribeRawSphere(RobustSphereFitResult fit)
    {
        string method = fit.UsesKnownRadius
            ? $"centre=KnownRadiusCenter nominalD={fit.NominalDiameterMm:F3}, freeDWithinMaxError={fit.IsDiameterWithinTolerance}, "
              + $"trainingN={fit.TrainingPointCount}, holdoutN={fit.HoldoutPointCount}, comparisonFreeD={fit.ComparisonFreeDiameterMm:F3}, "
            : "centre=FreeRadiusRobust, ";
        return method + $"rawQualityPassed={fit.IsGeometryValid}, sourceSupportPassed={fit.HasValidSourceSupport}, " +
            $"localizedValidation={fit.UsedLocalizedOutlierValidation}, centerSensitivity={fit.CenterSensitivityMm:F4}mm, " +
            $"fullFreeCoreRMS={fit.FullFreeCoreRmsMm:F4}, fullFreeCoreP95={fit.FullFreeCoreP95Mm:F4}, " +
            $"fullFreeOuterRMS={fit.FullFreeValidationRmsMm:F4}, fullFreeOuterP95={fit.FullFreeValidationP95Mm:F4}, " +
            $"boundedCoreRMS={fit.BoundedCoreRmsMm:F4}, boundedOuterRMS={fit.BoundedValidationRmsMm:F4}, " +
            $"coreGrossOutlierFraction={fit.CoreGrossOutlierFraction:P2}, outerGrossOutlierFraction={fit.ValidationGrossOutlierFraction:P2}, " +
            $"outerGrossWarning={fit.ValidationGrossOutlierFraction > .02}, outerInlierFraction={fit.ValidationInlierFraction:P2}, " +
            $"free D={fit.DiameterMm:F3}, coreRMS={fit.CoreRmsMm:F4}, " +
            $"outerRMS={fit.ValidationRmsMm:F4}, coreN={fit.CorePointCount}, " +
            $"outerN={fit.ValidationPointCount}, inliers={fit.InlierFraction:P1}";
    }

    private static void UpdateSphereDisplay(LSphereToolInfo info, RobustSphereFitResult fit)
    {
        string centerMethod = fit.UsesKnownRadius ? "標準尺寸求球心" : "穩健自由擬合";
        string diameterStatus = fit.IsGeometryValid
            ? BallSettings.IsExpectedDiameter(fit.DiameterMm) ? "合格" : "超差"
            : $"品質警告，{(BallSettings.IsExpectedDiameter(fit.DiameterMm) ? "數值區間內" : "超差")}";
        if (fit.UsedLocalizedOutlierValidation)
            centerMethod += $"；局部點品質警告；球心穩定性檢查通過，變動 {fit.CenterSensitivityMm:F3} mm";
        info.OverwriteValuesText(LSphereResultType.DetectSphereSphereCenterX, "X=" + fit.X.ToString("F3", CultureInfo.InvariantCulture) + " mm (" + centerMethod + ")");
        info.OverwriteValuesText(LSphereResultType.DetectSphereSphereCenterY, "Y=" + fit.Y.ToString("F3", CultureInfo.InvariantCulture) + " mm (" + centerMethod + ")");
        info.OverwriteValuesText(LSphereResultType.DetectSphereSphereCenterZ, "Z=" + fit.Z.ToString("F3", CultureInfo.InvariantCulture) + " mm (" + centerMethod + ")");
        info.OverwriteValuesText(LSphereResultType.DetectSphereSphereDiameter, "自由估徑=" + fit.DiameterMm.ToString("F3", CultureInfo.InvariantCulture) + " mm (" + diameterStatus + ")");
    }

    #endregion

    #region Functions

    public double ActiveRefPatternX
    {
        get
        {
            return _activeRefPatternX;
        }
    }
    public double ActiveRefPatternY
    {
        get
        {
            return _activeRefPatternY;
        }
    }
    public double ActiveRefSphereX
    {
        get
        {
            return _activeRefSphereX;
        }
    }
    public double ActiveRefSphereY
    {
        get
        {
            return _activeRefSphereY;
        }
    }

    /// <summary>工具結果的顯示文字。</summary>
    public string[] GetDisplayText(IList<ILToolInfo> results)
    {
        return results.Select(result => result.DisplayText()).ToArray();
    }

    /// <summary>SDK 原生工具數值；校正球心／球徑須使用 TryReadKeptSphere。</summary>
    public ILToolMeasureValues[] GetMeasureValues(IList<ILToolInfo> results)
    {
        return results.Select(result => result.ToolMeasureValues()).ToArray();
    }

    /// <summary>寫入掃描器事件日誌。</summary>
    public void LogScannerEvent(string text)
    {
        AppendLog(text);
    }

    private static Dictionary<LUnitType, string> CreateUnitDictionary()
    {
        return new Dictionary<LUnitType, string>
        {
            { LUnitType.None, "" },
            { LUnitType.Mm, "mm" },
            { LUnitType.Degree, "°" },
            { LUnitType.Mm2, "mm²" },
            { LUnitType.Mm3, "mm³" },
            { LUnitType.Number, "個" },
            { LUnitType.Percent, "%" },
        };
    }

    private static Dictionary<LSphereResultType, string> CreateSphereResultTypeList()
    {
        return new Dictionary<LSphereResultType, string>
        {
            { LSphereResultType.DetectSphereSphereCenterX, "中心X" },
            { LSphereResultType.DetectSphereSphereCenterY, "中心Y" },
            { LSphereResultType.DetectSphereSphereCenterZ, "中心Z" },
            { LSphereResultType.DetectSphereSphereDiameter, "直徑" },
        };
    }

    private static Dictionary<LPlaneResultType, string> CreatePlaneResultTypeList()
    {
        return new Dictionary<LPlaneResultType, string>
        {
            { LPlaneResultType.PlaneA, "平面A" },
            { LPlaneResultType.PlaneB, "平面B" },
            { LPlaneResultType.PlaneC, "平面C" },
        };
    }

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

    /// <summary>
    /// PatternMatch 常對同一顆球回傳多個角度命中。依分數高→低貪婪選取，
    /// 與已選中心距離 &lt; MinSphereCenterSeparation 視為同一顆，只留最高分。
    /// </summary>
    private static List<int> SelectUniquePatternMatchIndices(
        LEachPatternMatchResult[]? eachResults,
        ILEachMeasureValue[] pmEach,
        int rawCount)
    {
        var unique = new List<int>();
        if (eachResults is null || rawCount <= 0)
            return unique;

        var minSepSq = BallSettings.MinSphereCenterSeparationMm * BallSettings.MinSphereCenterSeparationMm;
        var ranked = Enumerable.Range(0, rawCount)
            .Select(i => (Index: i, Score: GetPmScore(pmEach, i)))
            .Where(t => t.Score > 0 && !double.IsNaN(t.Score))
            .OrderByDescending(t => t.Score)
            .ToList();

        var keptCenters = new List<(double X, double Y)>();
        foreach (var (index, _) in ranked)
        {
            var p = eachResults[index].AbsolutePosition.CenterPosition;
            var isDup = keptCenters.Any(k =>
            {
                var dx = p.X - k.X;
                var dy = p.Y - k.Y;
                return dx * dx + dy * dy < minSepSq;
            });
            if (isDup) continue;

            keptCenters.Add((p.X, p.Y));
            unique.Add(index);
        }

        return unique;
    }

    #endregion

    #region 建構函數

    private readonly LHeightImage _referenceHeightImage;
    private readonly LGrayImage _referenceGrayImage;
    private readonly string _logFile;

    /// <summary>以 Contents 資料夾載入參考影像與工具。</summary>
    public LjDeveloperMeasureCore(string contentsFolderPath)
    {
        _activeRefPatternX = RefPatternX;
        _activeRefPatternY = RefPatternY;
        _activeRefSphereX = RefSphereX;
        _activeRefSphereY = RefSphereY;

        _spheres = Enumerable.Range(0, PatternDetectCount).Select(_ => new LSphereTool()).ToArray();

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
    /// <summary>PM 固定搜尋 10 個候選，避免真球被重複角度或治具候選擠出；最終仍只保留 BallCount 顆。</summary>
    private const int PatternDetectCount = 10;
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
    private readonly Dictionary<ILToolInfo, RobustSphereFitResult> _keptSphereMeasurements
        = new(ReferenceEqualityComparer.Instance);

    #endregion
}
