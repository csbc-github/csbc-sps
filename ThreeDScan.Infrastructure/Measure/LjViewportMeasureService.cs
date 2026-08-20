using Keyence.LjDevCommon;
using Keyence.LjDevMeasure;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.LjSdk;

namespace ThreeDScan.Infrastructure.Measure;

/// <summary>
/// 對齊 LJScanWinform UpdateViewport：載入影像後 Execute，供 WPF Lj3DView 顯示。
/// </summary>
public static class LjViewportMeasureRunner
{
    public static LjViewportMeasureResult MeasureForViewport(
        string contentsFolder,
        string heightImagePath,
        string grayImagePath,
        ScannerConfigRecord config,
        LjCalibrationParameters parameters)
    {
        var settings = new LjMeasureSettings(parameters, config);
        BallSettings.SetScope(settings);
        try
        {
            var heightRefPath = Path.Combine(contentsFolder, "ref_height.png");
            if (!File.Exists(heightRefPath))
            {
                throw new InvalidOperationException(
                    $"找不到參考影像 {heightRefPath}。請先執行 POST /api/calibration/reference-setup。");
            }

            var measure = new LjDeveloperMeasureCore(contentsFolder);
            var height = new LHeightImage();
            height.Read(heightImagePath);
            var gray = new LGrayImage();
            gray.Read(grayImagePath);

            var toolResults = measure.Execute(height, gray);
            return new LjViewportMeasureResult
            {
                Height = height,
                Gray = gray,
                ToolResults = toolResults,
            };
        }
        finally
        {
            BallSettings.ClearScope();
        }
    }
}

public sealed class LjViewportMeasureResult
{
    public required LHeightImage Height { get; init; }
    public required LGrayImage Gray { get; init; }
    public required IList<ILToolInfo> ToolResults { get; init; }
}
