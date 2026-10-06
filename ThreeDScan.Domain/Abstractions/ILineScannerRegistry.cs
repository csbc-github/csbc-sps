using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>線掃描光達註冊表。只讀 appsettings「LineScanners」。</summary>
public interface ILineScannerRegistry
{
    /// <summary>依 scannerId 取得光達 IP 與埠。不在 LineScanners 時回傳 false。</summary>
    bool TryGet(string scannerId, out LineScannerEndpoint endpoint);
}
