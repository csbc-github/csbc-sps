namespace ThreeDScan.Infrastructure.LineScan.Hank;

public sealed record EncoderScanProgress(double PositionMm, double SpanMm, string Motion, long Samples);

/// <summary>A complete start/stop capture with frozen Encoder scale and automatic 3D output.</summary>
public sealed class EncoderScanSession
{
    readonly object gate = new();
    readonly Calibration calibration;
    readonly ReconstructionOptions settings;
    readonly EncoderTracker tracker;
    Task<Cloud>? completion;
    EncoderScanProgress progress = new(0, 0, "等待移動", 0);
    double minimum, maximum;
    public string RawPath { get; }
    public string OutputPath { get; }
    public Recorder Recorder { get; }
    public EncoderScanProgress Progress { get { lock (gate) return progress; } }

    EncoderScanSession(string path, Calibration calibration, ReconstructionOptions settings, object? captureContext)
    {
        if (settings.PositionSource != "encoder") throw new ArgumentException("開始／停止掃描須使用 Encoder 滾輪位移");
        this.calibration = calibration;
        this.settings = settings with { StartS = 0, EndS = null, RoiXY = settings.RoiXY?.ToArray() };
        StreamingReconstruction.Validate(calibration, this.settings);
        RawPath = Path.GetFullPath(path);
        if (!Path.GetExtension(RawPath).Equals(".ply", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("掃描檔案請使用 .ply 副檔名");
        string stem = Path.GetFileNameWithoutExtension(RawPath);
        if (stem.EndsWith("_raw", StringComparison.OrdinalIgnoreCase)) stem = stem[..^4];
        OutputPath = Path.Combine(Path.GetDirectoryName(RawPath)!, stem + "_3d.ply");
        foreach (string file in new[] { RawPath, Path.ChangeExtension(RawPath, ".encoder.jsonl"), Path.ChangeExtension(RawPath, ".capture.json"), OutputPath, Path.ChangeExtension(OutputPath, ".json") })
            if (File.Exists(file) || Directory.Exists(file)) throw new IOException("掃描輸出已存在，請選擇新的檔名：" + file);
        tracker = new(new(this.settings.EncoderCountsPerRev, 1, this.settings.EncoderChannel, 2 * Math.PI * this.settings.WheelRadiusMm));
        var context = new Dictionary<string, object?>();
        if (captureContext != null)
        {
            if (captureContext is IDictionary<string, object?> dictionary)
                foreach (var property in dictionary) context[property.Key] = property.Value;
            else
                foreach (var property in captureContext.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                    if (property.CanRead && property.GetIndexParameters().Length == 0) context[property.Name] = property.GetValue(captureContext);
        }
        context["calibration"] = new { first = new[] { calibration.First.X, calibration.First.Y }, second = new[] { calibration.Second.X, calibration.Second.Y } };
        context["roi_xy"] = this.settings.RoiXY;
        context["motion_settings"] = new { position_source = "encoder", wheel_radius_mm = this.settings.WheelRadiusMm,
            encoder_counts_per_rev = this.settings.EncoderCountsPerRev, encoder_channel = this.settings.EncoderChannel };
        context["reconstruction_settings"] = this.settings;
        context["workflow"] = "encoder_start_stop_3d";
        context["output_3d"] = OutputPath;
        Recorder = new(RawPath, context: context);
    }

    public static EncoderScanSession Start(string rawPath, Calibration calibration, ReconstructionOptions options, object? captureContext = null)
        => new(rawPath, calibration, options, captureContext);

    public void Submit(Scan scan)
    {
        lock (gate)
        {
            if (completion != null || Recorder.Error != null) return;
            try
            {
                var reading = tracker.Update(scan);
                if (reading.Rebased && progress.Samples > 0) throw new ArgumentException("Encoder 計數或設備時間中斷，請重新開始掃描");
                double position = settings.Direction * reading.TravelMm!.Value;
                minimum = Math.Min(minimum, position); maximum = Math.Max(maximum, position);
                long delta = settings.Direction * reading.Delta;
                progress = new(position, maximum - minimum, delta > 0 ? "前進" : delta < 0 ? "後退" : "停留", progress.Samples + 1);
                Recorder.Submit(scan);
            }
            catch (ArgumentException error) { Recorder.Fail(error.Message); }
        }
    }

    public Task<Cloud> StopAndBuildAsync()
    {
        lock (gate) return completion ??= Complete();
    }

    async Task<Cloud> Complete()
    {
        await Recorder.Stop().ConfigureAwait(false);
        if (Recorder.Error is string error) throw new InvalidDataException("錄製未完整完成：" + error);
        return await Task.Run(() => StreamingReconstruction.BuildAndSave(RawPath, OutputPath, calibration, settings)).ConfigureAwait(false);
    }
}
