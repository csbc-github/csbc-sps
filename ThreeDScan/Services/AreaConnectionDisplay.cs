using ThreeDScan.Models;

namespace ThreeDScan.Services;

/// <summary>各區連線狀態列的燈號與說明文字（TCP 至設定 Ip:Port，不含 Api）。</summary>
public static class AreaConnectionDisplay
{
    /// <summary>依 TCP 探測結果更新燈號與文字（僅 Normal／Error）。</summary>
    /// <param name="deviceConnected">對該區掃描器 Ip:Port 的 TCP 連線成功。</param>
    /// <param name="deviceLabel">設備名稱（含 scannerId）。</param>
    /// <param name="activitySuffix">作業中附加說明；空白則不附加。</param>
    /// <returns>燈號與完整說明。</returns>
    public static (SystemStatus Lamp, string Text) Build(
        bool deviceConnected,
        string deviceLabel,
        string activitySuffix)
    {
        if (!deviceConnected)
        {
            return (SystemStatus.Error, deviceLabel + " 未連線");
        }

        var text = deviceLabel + " 已連線";
        if (!string.IsNullOrWhiteSpace(activitySuffix))
        {
            text += " · " + activitySuffix.Trim();
        }

        return (SystemStatus.Normal, text);
    }
}
