using Keyence.LjDevCommon;

namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>擷取期間暫存高度圖與灰階，供後續量測使用。</summary>
internal static class ScanCaptureCache
{
    private static readonly Dictionary<string, (LHeightImage Height, LGrayImage Gray)> Cache = new();

    /// <summary>暫存本次擷取的高度圖與灰階。</summary>
    public static void Store(string captureId, LHeightImage height, LGrayImage gray)
    {
        Cache[captureId] = (height, gray);
    }

    /// <summary>依 captureId 取出暫存影像。</summary>
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

    /// <summary>清除暫存影像。</summary>
    public static void Remove(string captureId)
    {
        Cache.Remove(captureId);
    }
}
