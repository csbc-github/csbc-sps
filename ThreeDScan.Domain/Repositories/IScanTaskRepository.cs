using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Repositories;

/// <summary>
/// 掃描任務儲存（DB DocumentJson + ExportRoot/tasks/{id}/task.json）。
/// 對應 Hank <c>Scan3D.Service.TaskRepository</c>。
/// </summary>
public interface IScanTaskRepository
{
    /// <summary>回呼模式字串（Enabled／PollingOnly）。</summary>
    string CallbackMode { get; }

    /// <summary>任務檔根目錄（ExportRoot）。</summary>
    string Root { get; }

    /// <summary>任務目錄 <c>tasks/{id}</c>。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.TaskRepository</c>。</remarks>
    /// <param name="id">任務編號。</param>
    /// <returns>絕對路徑。</returns>
    string TaskDirectory(string id);

    /// <summary>依編號查找；不存在回 null。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.TaskRepository</c>。</remarks>
    /// <param name="id">任務編號。</param>
    /// <param name="ct">取消權杖。</param>
    Task<TaskDocument?> FindAsync(string id, CancellationToken ct = default);

    /// <summary>依編號取得；不存在或種類不符則丟例外。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.TaskRepository</c>。</remarks>
    /// <param name="id">任務編號。</param>
    /// <param name="kind">預期種類；null 不檢查。</param>
    /// <param name="ct">取消權杖。</param>
    Task<TaskDocument> GetAsync(string id, string? kind = null, CancellationToken ct = default);

    /// <summary>新建任務（檔與列皆不存在才可）。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.TaskRepository</c>。</remarks>
    /// <param name="doc">文件。</param>
    /// <param name="ct">取消權杖。</param>
    Task CreateAsync(TaskDocument doc, CancellationToken ct = default);

    /// <summary>在寫入鎖內變更並提交。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.TaskRepository</c>。</remarks>
    /// <param name="id">任務編號。</param>
    /// <param name="mutation">同步變更。</param>
    /// <param name="ct">取消權杖。</param>
    Task<TaskDocument> UpdateAsync(string id, Action<TaskDocument> mutation, CancellationToken ct = default);

    /// <summary>在寫入鎖內非同步變更並提交。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.TaskRepository</c>。</remarks>
    /// <param name="id">任務編號。</param>
    /// <param name="mutation">非同步變更。</param>
    /// <param name="ct">取消權杖。</param>
    Task<TaskDocument> CommitAsync(string id, Func<TaskDocument, Task> mutation, CancellationToken ct = default);

    /// <summary>未過保存期限的任務編號。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.TaskRepository</c>。</remarks>
    /// <param name="ct">取消權杖。</param>
    Task<IReadOnlyList<string>> TaskIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// 非終態任務編號（<see cref="TaskDocument.Terminal"/> 為 false；對應 DB Status 非 Completed／Failed／Aborted）。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    Task<IReadOnlyList<string>> NonTerminalTaskIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// 服務重啟恢復：將非終態任務標記 Failed／Interrupted 與 SERVICE_RESTARTED（Hank <c>RecoverAsync</c>）。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>實際更新的任務數。</returns>
    Task<int> RecoverAfterServiceRestartAsync(CancellationToken ct = default);

    /// <summary>組成對外信封。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.TaskRepository</c>。</remarks>
    /// <param name="document">文件。</param>
    TaskEnvelope Envelope(TaskDocument document);
}
