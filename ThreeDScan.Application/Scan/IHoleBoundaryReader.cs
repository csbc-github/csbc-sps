using ThreeDScan.Application.Scan.Models;

namespace ThreeDScan.Application.Scan;

/// <summary>讀取 API 寫出的最新孔洞邊界監看檔。</summary>
public interface IHoleBoundaryReader
{
    /// <summary>讀 ContentsRoot 下的 latest-hole-boundary.json；沒有檔或解析失敗回 null。</summary>
    /// <param name="contentsRoot">appsettings Storage:ContentsRoot。</param>
    /// <returns>邊界座標；無法讀取時為 null。</returns>
    HoleBoundarySnapshot? TryRead(string contentsRoot);
}
