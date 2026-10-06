using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>進料區框選範圍。沒有框選時量測使用全部點。</summary>
public interface ILineScanRegionStore
{
    /// <summary>讀取目前框選。尚未設定則為 null。</summary>
    LineScanRegion? Get();

    /// <summary>寫入框選。傳 null 表示清除。</summary>
    void Save(LineScanRegion? region);
}
