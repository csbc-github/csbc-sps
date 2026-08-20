using Keyence.LjDevCommunication;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Scanners;

internal static class Ljs8CaptureSettingsCodec
{
    /// <summary>半透明體在程式批次 blob 中的位元組偏移（依 program 快取，避免每次重探）。</summary>
    private static readonly Dictionary<byte, int> TranslucentBatchOffsets = new();
    /// <summary>UI 顯示與 Save 基準皆以 Save 深度為準。</summary>
    public static ScannerCaptureSettings Read(byte programNo, Ljs8DeviceSession session)
    {
        return ReadForSave(programNo, session).Settings;
    }

    /// <summary>Save 基準：LJ-S 拍攝參數以 Save 深度讀取。TriggerReadable 永遠為 false（LJ-S 無對應觸發節距欄位）。</summary>
    public static (ScannerCaptureSettings Settings, bool TriggerReadable) ReadForSave(byte programNo, Ljs8DeviceSession session)
    {
        var settings = ReadCapture(programNo, session, Ljs8ProgramConstants.SettingDepthSave);
        return (settings, false);
    }

    public static ScannerCaptureSettings ReadCapture(byte programNo, Ljs8DeviceSession session, byte depth)
    {
        byte Read1(byte cat, byte item)
        {
            return session.GetSetting(
                depth, Ljs8TargetBuilder.ProgramItem(programNo, cat, item), 1)[0];
        }

        var exposure = Read1(Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemExposureTime);
        var dynamicRange = Read1(Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemDynamicRange);
        var lightMode = Read1(Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemLightControlMode);
        var lightUpper = Read1(Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemLightControlUpper);
        var lightLower = Read1(Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemLightControlLower);
        var intervalY = Read1(Ljs8CaptureSettingTargets.CatInterval, Ljs8CaptureSettingTargets.ItemIntervalY);
        var peakSens = Read1(Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakSensitivity);
        var translucent = Read1(Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemTranslucent);
        var deadZone = Read1(Ljs8CaptureSettingTargets.CatProfile, Ljs8CaptureSettingTargets.ItemDeadZoneInterpolation);

        var peakSelBuf = session.GetSetting(
            depth,
            Ljs8TargetBuilder.ProgramItem(programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakSelection),
            Ljs8CaptureSettingTargets.PeakSelectionDataLength);

        var haloBuf = session.GetSetting(
            depth,
            Ljs8TargetBuilder.ProgramItem(programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakWidthFilter),
            Ljs8CaptureSettingTargets.PeakWidthFilterDataLength);

        return new ScannerCaptureSettings
        {
            ProgramNo = programNo,
            ExposureTimeIndex = exposure,
            DynamicRange = dynamicRange,
            LightControlMode = lightMode,
            LightControlUpper = lightUpper,
            LightControlLower = lightLower,
            YIntervalMultiplier = SdkToUiYInterval(intervalY),
            PeakDetectionSensitivity = peakSens,
            TranslucentPeakProcessing = translucent == 1,
            HaloFilterEnabled = haloBuf[0] == 1,
            HaloFilterStrength = (byte)Math.Clamp((int)haloBuf[1], 1, 5),
            DeadZoneInterpolation = deadZone,
            PeakSelection = peakSelBuf[0],
        };
    }

    private static byte SdkToUiYInterval(byte sdk)
    {
        return sdk <= 1 ? (byte)0 : sdk;
    }

    private static byte UiToSdkYInterval(byte ui)
    {
        return ui == 0 ? (byte)1 : ui;
    }

    public static void ApplyCaptureParameters(ScannerCaptureSettings target, ScannerCaptureSettings ui)
    {
        target.DynamicRange = ui.DynamicRange;
        target.ExposureTimeIndex = ui.ExposureTimeIndex;
        target.LightControlMode = ui.LightControlMode;
        target.LightControlUpper = ui.LightControlUpper;
        target.LightControlLower = ui.LightControlLower;
        target.YIntervalMultiplier = ui.YIntervalMultiplier;
        target.PeakDetectionSensitivity = ui.PeakDetectionSensitivity;
        target.TranslucentPeakProcessing = ui.TranslucentPeakProcessing;
        target.HaloFilterEnabled = ui.HaloFilterEnabled;
        target.HaloFilterStrength = ui.HaloFilterStrength;
        target.PeakSelection = ui.PeakSelection;
        target.DeadZoneInterpolation = ui.DeadZoneInterpolation;
        // YDataIntervalMm 僅顯示，不寫入控制器
    }

    public static bool CaptureParametersEqual(ScannerCaptureSettings a, ScannerCaptureSettings b)
    {
        return a.DynamicRange == b.DynamicRange
           && a.ExposureTimeIndex == b.ExposureTimeIndex
           && a.LightControlMode == b.LightControlMode
           && a.LightControlUpper == b.LightControlUpper
           && a.LightControlLower == b.LightControlLower
           && a.YIntervalMultiplier == b.YIntervalMultiplier
           && a.PeakDetectionSensitivity == b.PeakDetectionSensitivity
           && a.TranslucentPeakProcessing == b.TranslucentPeakProcessing
           && a.HaloFilterEnabled == b.HaloFilterEnabled
           && a.HaloFilterStrength == b.HaloFilterStrength
           && a.PeakSelection == b.PeakSelection
           && a.DeadZoneInterpolation == b.DeadZoneInterpolation;
    }

    /// <summary>LJ-S 無對應觸發節距欄位，保留簽章供服務層呼叫，永遠視為相同。</summary>
    public static void ApplyTriggerParameters(ScannerCaptureSettings target, ScannerCaptureSettings ui) { }

    public static bool TriggerParametersEqual(ScannerCaptureSettings a, ScannerCaptureSettings b)
    {
        return true;
    }

    public static ScannerCaptureSettings Clone(ScannerCaptureSettings source)
    {
        return new()
        {
            ProgramNo = source.ProgramNo,
            DynamicRange = source.DynamicRange,
            ExposureTimeIndex = source.ExposureTimeIndex,
            LightControlMode = source.LightControlMode,
            LightControlUpper = source.LightControlUpper,
            LightControlLower = source.LightControlLower,
            YIntervalMultiplier = source.YIntervalMultiplier,
            YDataIntervalMm = source.YDataIntervalMm,
            PeakDetectionSensitivity = source.PeakDetectionSensitivity,
            TranslucentPeakProcessing = source.TranslucentPeakProcessing,
            HaloFilterEnabled = source.HaloFilterEnabled,
            HaloFilterStrength = source.HaloFilterStrength,
            PeakSelection = source.PeakSelection,
            DeadZoneInterpolation = source.DeadZoneInterpolation,
        };
    }

    public static void WriteChangesToSession(
        byte programNo,
        ScannerCaptureSettings baseline,
        ScannerCaptureSettings merged,
        Ljs8DeviceSession session,
        bool includeTrigger = true)
    {
        int? translucentOffset = null;
        if (baseline.TranslucentPeakProcessing != merged.TranslucentPeakProcessing)
            translucentOffset = FindTranslucentBatchOffset(programNo, session);

        if (baseline.ExposureTimeIndex != merged.ExposureTimeIndex)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemExposureTime, merged.ExposureTimeIndex);
        if (baseline.DynamicRange != merged.DynamicRange)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemDynamicRange, merged.DynamicRange);
        if (baseline.LightControlMode != merged.LightControlMode)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemLightControlMode, merged.LightControlMode);
        if (baseline.LightControlUpper != merged.LightControlUpper)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemLightControlUpper, merged.LightControlUpper);
        if (baseline.LightControlLower != merged.LightControlLower)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemLightControlLower, merged.LightControlLower);
        if (baseline.YIntervalMultiplier != merged.YIntervalMultiplier)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatInterval, Ljs8CaptureSettingTargets.ItemIntervalY, UiToSdkYInterval(merged.YIntervalMultiplier));
        if (baseline.PeakDetectionSensitivity != merged.PeakDetectionSensitivity)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakSensitivity, merged.PeakDetectionSensitivity);

        if (baseline.PeakSelection != merged.PeakSelection)
            WritePeakSelection(session, programNo, merged.PeakSelection);
        if (baseline.HaloFilterEnabled != merged.HaloFilterEnabled
            || baseline.HaloFilterStrength != merged.HaloFilterStrength)
            WriteHaloFilter(session, programNo, merged.HaloFilterEnabled, merged.HaloFilterStrength);

        if (baseline.DeadZoneInterpolation != merged.DeadZoneInterpolation)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatProfile, Ljs8CaptureSettingTargets.ItemDeadZoneInterpolation, merged.DeadZoneInterpolation);

        // 半透明體單項 SetSetting 皆 0x8032；須於其他逐項寫入完成後，以批次 blob 修補。
        if (baseline.TranslucentPeakProcessing != merged.TranslucentPeakProcessing)
        {
            if (translucentOffset is null)
                throw new InvalidOperationException(
                    "無法在程式批次設定中定位半透明體位元組（Cat 05 Item 02 不支援單項 SetSetting）");
            WriteTranslucentViaBatch(programNo, session, translucentOffset.Value, merged.TranslucentPeakProcessing);
        }
    }

    private static void WriteScalar(Ljs8DeviceSession session, byte programNo, byte cat, byte item, byte value)
    {
        var length = Ljs8CaptureSettingTargets.GetSetDataLength(cat, item);
        var data = new byte[length];
        data[0] = value;
        session.SetWrite(Ljs8TargetBuilder.ProgramItem(programNo, cat, item), data);
    }

    /// <summary>
    /// 在 10980-byte 程式批次中定位半透明體位元組：以 SAVE 讀值比對 blob，
    /// 並在峰值選擇 16-byte 區塊附近縮小候選，再以 SetBatchWrite 探測驗證。
    /// </summary>
    private static int? FindTranslucentBatchOffset(byte programNo, Ljs8DeviceSession session)
    {
        var target = Ljs8TargetBuilder.ProgramItem(
            programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemTranslucent);

        byte current;
        try
        {
            current = session.GetSetting(Ljs8ProgramConstants.SettingDepthSave, target, 1)[0];
        }
        catch
        {
            return null;
        }

        var saveBatch = session.GetBatch(Ljs8ProgramConstants.SettingDepthSave, programNo);

        if (TranslucentBatchOffsets.TryGetValue(programNo, out var cached)
            && cached >= 0
            && cached < saveBatch.Length
            && saveBatch[cached] == current)
        {
            // #region agent log
            Ljs8AgentDebugLog.Write(
                "FindTranslucentBatchOffset",
                "cache hit",
                new { idx = cached, current },
                "H4-batch");
            // #endregion
            return cached;
        }

        var peakPattern = session.GetSetting(
            Ljs8ProgramConstants.SettingDepthSave,
            Ljs8TargetBuilder.ProgramItem(
                programNo,
                Ljs8CaptureSettingTargets.CatPeak,
                Ljs8CaptureSettingTargets.ItemPeakSelection),
            Ljs8CaptureSettingTargets.PeakSelectionDataLength);

        var peakBlock = FindEmbeddedBlockOffset(saveBatch, peakPattern);
        List<int> candidates;
        if (peakBlock is int pb)
        {
            var from = Math.Max(0, pb);
            var to = Math.Min(saveBatch.Length, pb + Ljs8CaptureSettingTargets.PeakSelectionDataLength + 48);
            candidates = FindByteOffsets(saveBatch, current, from, to);

            // 實測半透明體緊接在峰值選擇 16-byte 區塊之後（peakBlock + 16）。
            var preferred = pb + Ljs8CaptureSettingTargets.PeakSelectionDataLength;
            if (preferred < saveBatch.Length && saveBatch[preferred] == current)
                candidates = PrioritizeOffset(candidates, preferred);
        }
        else
        {
            candidates = FindByteOffsets(saveBatch, current);
        }

        if (candidates.Count == 0)
        {
            // #region agent log
            Ljs8AgentDebugLog.Write(
                "FindTranslucentBatchOffset",
                "no candidates",
                new { current, peakBlock },
                "H4-batch");
            // #endregion
            return null;
        }

        var probe = (byte)(current == 0 ? 1 : 0);
        var baselineWrite = session.GetBatch(Ljs8ProgramConstants.SettingDepthWrite, programNo);

        foreach (var idx in candidates)
        {
            var trial = (byte[])baselineWrite.Clone();
            trial[idx] = probe;

            if (!session.TrySetBatchWrite(programNo, trial, out var rc, out _))
            {
                // #region agent log
                Ljs8AgentDebugLog.Write(
                    "FindTranslucentBatchOffset",
                    "probe SetBatchWrite rejected",
                    new { idx, probe, rc },
                    "H4-batch");
                // #endregion
                session.RewriteWriteBufferFrom(Ljs8ProgramConstants.SettingDepthSave);
                baselineWrite = session.GetBatch(Ljs8ProgramConstants.SettingDepthWrite, programNo);
                continue;
            }

            if (session.TryGetSetting(Ljs8ProgramConstants.SettingDepthWrite, target, 1, out var rb)
                && rb[0] == probe)
            {
                // #region agent log
                Ljs8AgentDebugLog.Write(
                    "FindTranslucentBatchOffset",
                    "found",
                    new { idx, current, probe, candidateCount = candidates.Count, peakBlock },
                    "H4-batch");
                // #endregion
                TranslucentBatchOffsets[programNo] = idx;
                session.RewriteWriteBufferFrom(Ljs8ProgramConstants.SettingDepthSave);
                return idx;
            }

            session.RewriteWriteBufferFrom(Ljs8ProgramConstants.SettingDepthSave);
            baselineWrite = session.GetBatch(Ljs8ProgramConstants.SettingDepthWrite, programNo);
        }

        session.RewriteWriteBufferFrom(Ljs8ProgramConstants.SettingDepthSave);

        // #region agent log
        Ljs8AgentDebugLog.Write(
            "FindTranslucentBatchOffset",
            "probe exhausted",
            new { current, candidateCount = candidates.Count, peakBlock },
            "H4-batch");
        // #endregion
        return null;
    }

    private static List<int> PrioritizeOffset(List<int> candidates, int preferred)
    {
        if (candidates.Contains(preferred))
            return [preferred, .. candidates.Where(c => c != preferred)];
        return candidates;
    }

    private static void WriteTranslucentViaBatch(
        byte programNo,
        Ljs8DeviceSession session,
        int offset,
        bool enabled)
    {
        var batch = session.GetBatch(Ljs8ProgramConstants.SettingDepthWrite, programNo);
        var value = (byte)(enabled ? 1 : 0);
        batch[offset] = value;

        // #region agent log
        Ljs8AgentDebugLog.Write(
            "WriteTranslucentViaBatch",
            "SetBatchWrite patch",
            new { offset, value, batchLen = batch.Length },
            "H4-batch");
        // #endregion

        session.SetBatchWrite(programNo, batch);

        var target = Ljs8TargetBuilder.ProgramItem(
            programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemTranslucent);
        if (!session.TryGetSetting(Ljs8ProgramConstants.SettingDepthWrite, target, 1, out var rb)
            || rb[0] != value)
        {
            throw new InvalidOperationException(
                $"半透明體批次修補後讀回不符（offset={offset}，預期={value}，實際={(rb.Length > 0 ? rb[0].ToString() : "?")}）");
        }
    }

    private static int? FindEmbeddedBlockOffset(byte[] batch, byte[] pattern)
    {
        if (pattern.Length == 0 || batch.Length < pattern.Length)
            return null;

        for (var i = 0; i <= batch.Length - pattern.Length; i++)
        {
            if (batch.AsSpan(i, pattern.Length).SequenceEqual(pattern))
                return i;
        }

        return null;
    }

    private static List<int> FindByteOffsets(byte[] batch, byte value, int start = 0, int end = int.MaxValue)
    {
        var list = new List<int>();
        var to = Math.Min(end, batch.Length);
        for (var i = Math.Max(0, start); i < to; i++)
        {
            if (batch[i] == value)
                list.Add(i);
        }

        return list;
    }

    /// <summary>峰值選擇為 16 byte 結構，僅 byte0 為峰值選擇；以讀-改-寫保留其餘設定。</summary>
    private static void WritePeakSelection(Ljs8DeviceSession session, byte programNo, byte selection)
    {
        var target = Ljs8TargetBuilder.ProgramItem(
            programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakSelection);

        if (!session.TryGetSetting(
                Ljs8ProgramConstants.SettingDepthWrite,
                target,
                Ljs8CaptureSettingTargets.PeakSelectionDataLength,
                out var buf))
        {
            if (!session.TryGetSetting(
                    Ljs8ProgramConstants.SettingDepthSave,
                    target,
                    Ljs8CaptureSettingTargets.PeakSelectionDataLength,
                    out buf))
            {
                buf = new byte[Ljs8CaptureSettingTargets.PeakSelectionDataLength];
            }
        }

        buf[0] = selection;
        session.SetWrite(target, buf);
    }

    private static void WriteHaloFilter(Ljs8DeviceSession session, byte programNo, bool enabled, byte strength)
    {
        var length = Ljs8CaptureSettingTargets.GetSetDataLength(
            Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakWidthFilter);
        var data = new byte[length];
        data[0] = (byte)(enabled ? 1 : 0);
        data[1] = (byte)Math.Clamp((int)strength, 1, 5);
        session.SetWrite(
            Ljs8TargetBuilder.ProgramItem(
                programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakWidthFilter),
            data);
    }
}
