using Keyence.LjDevCommon;

namespace ThreeDScan.Infrastructure.Scanners;

internal static class ScanCaptureCache
{
    private static readonly Dictionary<string, (LHeightImage Height, LGrayImage Gray)> Cache = new();

    public static void Store(string captureId, LHeightImage height, LGrayImage gray)
    {
        Cache[captureId] = (height, gray);
    }

    public static bool TryGet(string captureId, out LHeightImage height, out LGrayImage gray)
    {
        if (Cache.TryGetValue(captureId, out var pair))
        {
            height = pair.Height;
            gray = pair.Gray;
            return true;
        }
        height = default;
        gray = default;
        return false;
    }

    public static void Remove(string captureId)
    {
        Cache.Remove(captureId);
    }
}
