using Keyence.LjDevCommon;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// 把 KEYENCE <see cref="LHeightImage"/>（與可選灰階）轉成 Hank <see cref="HeightFrame"/>。
/// </summary>
/// <remarks>
/// Pitch／Start 以 mm×100000 寫入，對齊 Hank <c>HeightInfo</c>。
/// 正式寫 PLY 請走 <see cref="HeightFramePlyExporter"/>，不要再用 <see cref="HeightImagePlyExporter"/>。
/// </remarks>
public static class HeightImageFrameAdapter
{
    /// <summary>由高度圖（與可選灰階）組成 HeightFrame。</summary>
    /// <param name="heightImage">SDK 高度圖。</param>
    /// <param name="grayImage">可選灰階；長度不符時略過亮度。</param>
    /// <param name="imageNo">SDK 影像編號。</param>
    /// <param name="firstProfileNo">第一條剖面編號。</param>
    /// <returns>可供 SavePly 使用的高度圖。</returns>
    public static HeightFrame FromLHeightImage(
        LHeightImage heightImage,
        LGrayImage? grayImage = null,
        uint imageNo = 0,
        uint firstProfileNo = 0)
    {
        var source = heightImage.Source
            ?? throw new InvalidOperationException("Height image Source is null.");
        if (source.Length == 0)
        {
            throw new InvalidOperationException("Height image is empty.");
        }

        var xSize = heightImage.XSize;
        var ySize = heightImage.YSize;
        if (xSize <= 0 || ySize <= 0)
        {
            throw new InvalidDataException("Height image XSize／YSize 必須大於 0。");
        }

        if (source.Length != checked(xSize * ySize))
        {
            throw new InvalidDataException("Height image Source 長度與 XSize×YSize 不符。");
        }

        byte[]? luminance = null;
        byte luminanceOutput = 0;
        if (grayImage?.Source is { Length: > 0 } graySource
            && graySource.Length == source.Length)
        {
            luminance = graySource;
            luminanceOutput = 1;
        }

        return new HeightFrame
        {
            Info = new HeightInfo
            {
                XPointCount = (ushort)xSize,
                YLineCount = (ushort)ySize,
                LuminanceOutput = luminanceOutput,
                XStart = ToScaledInt(heightImage.ImagePosition.X),
                PitchX = ToScaledUInt(heightImage.CoefficientX),
                YStart = ToScaledInt(heightImage.ImagePosition.Y),
                PitchY = ToScaledUInt(heightImage.CoefficientY),
                PitchZ = ToScaledUInt(heightImage.CoefficientZ),
            },
            ImageNo = imageNo,
            FirstProfileNo = firstProfileNo,
            ProfileCount = ySize,
            Heights = source,
            Luminance = luminance,
        };
    }

    /// <summary>mm → Hank 整數欄位（×100000）。</summary>
    private static int ToScaledInt(double millimetres)
    {
        return (int)Math.Round(millimetres * 100000d, MidpointRounding.AwayFromZero);
    }

    /// <summary>mm 間距 → Hank 無號欄位（×100000）。</summary>
    private static uint ToScaledUInt(double millimetres)
    {
        var scaled = Math.Round(Math.Abs(millimetres) * 100000d, MidpointRounding.AwayFromZero);
        if (scaled < 1)
        {
            throw new InvalidDataException("Height image coefficient 換算後 pitch 為 0。");
        }

        return (uint)scaled;
    }
}
