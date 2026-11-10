using System.Text.Json;

namespace ThreeDScan.Infrastructure.LineScan.Hank;

public record ReconstructionOptions
{
    public double TravelMm { get; init; } = 300;
    public double StartS { get; init; }
    public double? EndS { get; init; }
    public double Xmin { get; init; } = -1000;
    public double Xmax { get; init; } = 1000;
    public double MinHeight { get; init; } = 3;
    public double MaxHeight { get; init; } = 500;
    public int Direction { get; init; } = 1;
    public int AverageScans { get; init; } = 1;
    public double[]? RoiXY { get; init; }
    public string PositionSource { get; init; } = "time";
    public double WheelRadiusMm { get; init; } = 100;
    public int EncoderCountsPerRev { get; init; } = 1024;
    public int EncoderChannel { get; init; } = 1;
}

public static class ReconstructionIO
{
    public static Cloud Build(Cloud raw, Calibration calibration, ReconstructionOptions settings) =>
        raw.Reconstruct(calibration, settings.TravelMm, settings.AverageScans, settings.StartS,
            settings.EndS, settings.MinHeight, settings.MaxHeight, settings.Xmin, settings.Xmax,
            settings.Direction, settings.RoiXY, settings.PositionSource, settings.WheelRadiusMm,
            settings.EncoderCountsPerRev, settings.EncoderChannel);

    public static void Save(string destination, Cloud cloud, string source, Calibration calibration, ReconstructionOptions settings)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        string metadata = Path.ChangeExtension(destination, ".json");
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) || File.Exists(destination) || File.Exists(metadata))
            throw new IOException("輸出檔已存在或與原始檔相同；請選新檔名");
        bool plyCreated = false, metadataCreated = false;
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, "reconstruct_" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            cloud.Write(temporary);
            File.Move(temporary, destination, overwrite: false);
            plyCreated = true;
            using var output = new FileStream(metadata, FileMode.CreateNew);
            metadataCreated = true;
            var report = new Dictionary<string, object?>(cloud.ReconstructionReport ?? new())
            {
                ["source"] = source,
                ["output"] = destination,
                ["units"] = "mm",
                ["position_method"] = cloud.PositionMethod,
                ["calibration"] = new { first = new[] { calibration.First.X, calibration.First.Y }, second = new[] { calibration.Second.X, calibration.Second.Y } },
                ["settings"] = new { travel_mm = settings.TravelMm, start_s = settings.StartS, end_s = settings.EndS,
                    xmin = settings.Xmin, xmax = settings.Xmax, min_height = settings.MinHeight, max_height = settings.MaxHeight,
                    direction = settings.Direction, average_scans = settings.AverageScans, roi_xy = settings.RoiXY,
                    position_source = settings.PositionSource, wheel_radius_mm = settings.WheelRadiusMm,
                    encoder_counts_per_rev = settings.EncoderCountsPerRev, encoder_channel = settings.EncoderChannel },
                ["dimensions"] = cloud.Dimensions(),
                ["output_frames"] = cloud.Points.Select(p => p.Frame).Distinct().Count(),
                ["output_points"] = cloud.Points.Count
            };
            if (cloud.PositionMethod == "encoder_roll_travel") report["simulation"] = cloud.Simulation;
            JsonSerializer.Serialize(output, report, ScanJson.Options);
        }
        catch
        {
            if (metadataCreated) File.Delete(metadata);
            if (plyCreated && File.Exists(destination)) File.Delete(destination);
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
