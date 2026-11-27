using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>Validate the native dimensions/headers before consuming packed height data.</summary>
internal static class HoleCaptureFrameBuilder
{
    internal static HeightFrame Build(HoleNativeHeightRequest request, HoleNativeHeightResponse response,
        HoleNativeHeightInfo info, HoleNativeProfileHeader[] headers, ushort[] heights, byte[] luminance)
    {
        if (request.ProfileCount is < 1 or > HoleCaptureAcquisition.RequestedProfiles
            || info.XPointCount is 0 or > HeightFramePlyExporter.MaxWidth
            || response.ProfileCount == 0 || response.ProfileCount > request.ProfileCount
            || headers.Length < response.ProfileCount)
            throw new InvalidDataException("SDK 回傳的資料尺寸超出緩衝區範圍。");
        for (int i = 0; i < response.ProfileCount; i++)
            if (headers[i].ImageNo != response.ImageNo || headers[i].ProfileNo != response.TopProfileNo + (uint)i)
                throw new InvalidDataException("輪廓標頭的影像編號或順序不一致。");
        int size = checked(info.XPointCount * response.ProfileCount);
        if (heights.Length < size || info.LuminanceOutput == 1 && luminance.Length < size)
            throw new InvalidDataException("SDK 高度或亮度緩衝區長度不足。");
        Array.Resize(ref heights, size);
        if (info.LuminanceOutput == 1) Array.Resize(ref luminance, size);
        return new HeightFrame
        {
            Info = new HeightInfo { XPointCount = info.XPointCount, YLineCount = info.YLineCount,
                LuminanceOutput = info.LuminanceOutput, XStart = info.XStart, YStart = info.YStart,
                PitchX = info.PitchX, PitchY = info.PitchY, PitchZ = info.PitchZ },
            ImageNo = response.ImageNo, FirstProfileNo = response.TopProfileNo,
            ProfileCount = response.ProfileCount, Heights = heights,
            Luminance = info.LuminanceOutput == 1 ? luminance : null,
        };
    }
}
