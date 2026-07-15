using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>掃描器註冊表：解析 appsettings「Scanners」中的連線端點與內容目錄路徑。</summary>
public interface IScannerRegistry
{
    /// <summary>
    /// 依 ScannerId 查詢 appsettings「Scanners」註冊表，取得連線端點（IP、Port、預設 Scan 群組等）。
    /// </summary>
    /// <returns>找到回傳 true 並填入 <paramref name="endpoint"/>；未知 ScannerId 回傳 false。</returns>
    bool TryGetEndpoint(string scannerId, out ScannerEndpoint endpoint);

    /// <summary>
    /// 將 Navigator 拍攝群組編號（Scan 1～16）轉成 LJ SDK 程式索引 programNo（0～15）。
    /// 優先使用 <paramref name="navigatorScanNo"/>；省略時改讀該掃描器註冊預設；皆無則回傳 0。
    /// </summary>
    byte ResolveProgramNo(string scannerId, int? navigatorScanNo);

    /// <summary>掃描器 Ref 圖與 LJ 量測根目錄（每台一筆，不含 program 子目錄）。</summary>
    string ResolveScannerContentsFolder(string scannerId, string contentsRoot);

    /// <summary>拍攝群組預覽／暫存子目錄（依 programNo）。</summary>
    string ResolveContentsFolder(string scannerId, byte programNo, string contentsRoot);
}
