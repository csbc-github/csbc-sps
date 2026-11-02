using System.Globalization;
using System.Text;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// 將 <see cref="HeightFrame"/> 寫成 ASCII PLY。
/// </summary>
/// <remarks>
/// 來源：Hank <c>Scan3D.Scanner.PointCloudExporter.SavePly</c>（FULL_Project/Scan3D）。
/// Z＝(raw−32768)×(PitchZ/100000)；無效點 raw＝0；平面原點依 <see cref="PointCloudOrigin"/>。
/// </remarks>
public static class HeightFramePlyExporter
{
    /// <summary>KEYENCE LJS8 單列最大點數（Hank <c>KeyenceSdk.MaxWidth</c>）。</summary>
    public const int MaxWidth = 3200;

    /// <summary>檢查高度圖尺寸、緩衝與 pitch 是否一致。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Scanner.PointCloudExporter</c>。</remarks>
    /// <param name="frame">高度圖。</param>
    public static void Validate(HeightFrame frame)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        var size = checked(frame.Info.XPointCount * frame.ProfileCount);
        if (frame.Info.XPointCount is 0 or > MaxWidth
            || frame.ProfileCount <= 0
            || frame.Heights.Length != size
            || (frame.Luminance is not null && frame.Luminance.Length != size)
            || (frame.Info.LuminanceOutput == 1) != (frame.Luminance is not null))
        {
            throw new InvalidDataException("Height frame dimensions and buffers do not agree.");
        }

        if (frame.Info.PitchX == 0 || frame.Info.PitchY == 0 || frame.Info.PitchZ == 0)
        {
            throw new InvalidDataException("Height frame pitch must be nonzero on all axes.");
        }
    }

    /// <summary>寫出 ASCII PLY（座標 mm）。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Scanner.PointCloudExporter</c>。</remarks>
    /// <param name="frame">高度圖。</param>
    /// <param name="path">目標路徑；副檔名不是 .ply 時會補上。</param>
    /// <param name="origin">平面原點。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>路徑、頂點數、檔案大小。</returns>
    public static PlyExportResult SavePly(
        HeightFrame frame,
        string path,
        PointCloudOrigin origin = PointCloudOrigin.Scanner,
        CancellationToken cancellationToken = default)
    {
        Validate(frame);
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var vertices = 0;
        foreach (var raw in frame.Heights)
        {
            if (raw != 0)
            {
                vertices++;
            }
        }

        if (vertices == 0)
        {
            throw new InvalidDataException("Every height pixel is invalid (raw=0).");
        }

        if (!path.EndsWith(".ply", StringComparison.OrdinalIgnoreCase))
        {
            path += ".ply";
        }

        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var pitchX = frame.Info.PitchX / 100000d;
        var pitchY = frame.Info.PitchY / 100000d;
        var pitchZ = frame.Info.PitchZ / 100000d;
        var originX = origin == PointCloudOrigin.Scanner ? frame.Info.XStart / 100000d : 0;
        var originY = origin == PointCloudOrigin.Scanner ? frame.Info.YStart / 100000d : 0;
        var invariant = CultureInfo.InvariantCulture;
        try
        {
            using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false), 1024 * 1024))
            {
                writer.NewLine = "\n";
                writer.WriteLine("ply\nformat ascii 1.0");
                writer.WriteLine("comment KEYENCE LJS8; coordinates in mm; frame=scanner");
                writer.WriteLine($"comment coordinate_origin {origin}");
                writer.WriteLine(FormattableString.Invariant(
                    $"comment sdk_origin_mm {frame.Info.XStart / 100000d:G17} {frame.Info.YStart / 100000d:G17}"));
                writer.WriteLine(FormattableString.Invariant(
                    $"comment pitch_mm {pitchX:G17} {pitchY:G17} {pitchZ:G17}"));
                writer.WriteLine($"comment image_no {frame.ImageNo}");
                writer.WriteLine($"element vertex {vertices.ToString(invariant)}");
                writer.WriteLine("property float x\nproperty float y\nproperty float z");
                if (frame.Luminance is not null)
                {
                    writer.WriteLine("property uchar intensity");
                }

                writer.WriteLine("end_header");
                for (var y = 0; y < frame.ProfileCount; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var x = 0; x < frame.Info.XPointCount; x++)
                    {
                        var index = y * frame.Info.XPointCount + x;
                        var raw = frame.Heights[index];
                        if (raw == 0)
                        {
                            continue;
                        }

                        writer.Write((originX + x * pitchX).ToString("F6", invariant));
                        writer.Write(' ');
                        writer.Write((originY + (frame.FirstProfileNo + y) * pitchY).ToString("F6", invariant));
                        writer.Write(' ');
                        writer.Write(((raw - 32768) * pitchZ).ToString("F6", invariant));
                        if (frame.Luminance is not null)
                        {
                            writer.Write(' ');
                            writer.Write(frame.Luminance[index].ToString(invariant));
                        }

                        writer.WriteLine();
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, false);
            return new PlyExportResult
            {
                Path = path,
                VertexCount = vertices,
                FileBytes = new FileInfo(path).Length,
            };
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
