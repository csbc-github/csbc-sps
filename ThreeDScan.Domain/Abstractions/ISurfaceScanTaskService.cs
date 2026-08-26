using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>面掃描任務識別與附件路徑（寫在 ExportRoot，非資料庫）。</summary>
public interface ISurfaceScanTaskService
{
    /// <summary>建立尚未掃描的任務（seq=0）；成功掃描後再 <see cref="Save"/>。</summary>
    SurfaceScanTaskRecord Create(string scannerId);

    /// <summary>依 taskId 讀取任務；不存在回傳 false。</summary>
    bool TryGet(string taskId, out SurfaceScanTaskRecord? record);

    /// <summary>寫入記憶體快取與 ExportRoot 檔案。</summary>
    void Save(SurfaceScanTaskRecord record);
}
