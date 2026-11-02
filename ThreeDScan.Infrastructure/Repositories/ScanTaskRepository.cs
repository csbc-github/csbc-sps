using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Repositories.Models;

namespace ThreeDScan.Infrastructure.Repositories;

/// <summary>
/// 任務儲存：SQL Server 一列 DocumentJson，並原子寫入 ExportRoot/tasks/{id}/task.json。
/// </summary>
/// <remarks>來源：Hank <c>Scan3D.Service.TaskRepository</c>。</remarks>
public sealed class ScanTaskRepository : IScanTaskRepository
{
    private readonly ThreeDScanDbContext _db;
    private readonly ScanTaskGate _gate;

    /// <summary>建立任務倉儲。</summary>
    /// <param name="db">資料庫內容。</param>
    /// <param name="gate">跨 scope 寫入鎖。</param>
    /// <param name="storage">ExportRoot。</param>
    /// <param name="configuration">讀 <c>Callback:Mode</c>。</param>
    public ScanTaskRepository(
        ThreeDScanDbContext db,
        ScanTaskGate gate,
        IStorageSettings storage,
        IConfiguration configuration)
    {
        _db = db;
        _gate = gate;
        var mode = configuration["Callback:Mode"];
        CallbackMode = string.IsNullOrWhiteSpace(mode) ? "Enabled" : mode;
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storage.ExportRoot));
        Directory.CreateDirectory(Path.Combine(Root, "tasks"));
    }

    /// <inheritdoc />
    public string CallbackMode { get; }

    /// <inheritdoc />
    public string Root { get; }

    /// <inheritdoc />
    public string TaskDirectory(string id)
    {
        ValidateId(id);
        return Path.Combine(Root, "tasks", id);
    }

    /// <inheritdoc />
    public async Task<TaskDocument?> FindAsync(string id, CancellationToken ct = default)
    {
        ValidateId(id);
        var gate = _gate.For(id);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadUnlockedAsync(id, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<TaskDocument> GetAsync(string id, string? kind = null, CancellationToken ct = default)
    {
        var doc = await FindAsync(id, ct).ConfigureAwait(false);
        if (doc is null)
        {
            throw Missing(kind);
        }

        if (kind is not null && doc.Kind != kind)
        {
            throw Missing(kind);
        }

        return doc;
    }

    /// <inheritdoc />
    public async Task CreateAsync(TaskDocument doc, CancellationToken ct = default)
    {
        if (doc is null)
        {
            throw new ArgumentNullException(nameof(doc));
        }

        ValidateId(doc.TaskId);
        var gate = _gate.For(doc.TaskId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (await ExistsUnlockedAsync(doc.TaskId, ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Duplicate task ID");
            }

            Directory.CreateDirectory(TaskDirectory(doc.TaskId));
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistUnlockedAsync(doc, insert: true, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<TaskDocument> UpdateAsync(string id, Action<TaskDocument> mutation, CancellationToken ct = default)
    {
        return CommitAsync(id, doc =>
        {
            mutation(doc);
            return Task.CompletedTask;
        }, ct);
    }

    /// <inheritdoc />
    public async Task<TaskDocument> CommitAsync(
        string id,
        Func<TaskDocument, Task> mutation,
        CancellationToken ct = default)
    {
        ValidateId(id);
        var gate = _gate.For(id);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var doc = await ReadUnlockedAsync(id, ct).ConfigureAwait(false);
            if (doc is null)
            {
                throw Missing(null);
            }

            await mutation(doc).ConfigureAwait(false);
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistUnlockedAsync(doc, insert: false, ct).ConfigureAwait(false);
            return doc;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> TaskIdsAsync(CancellationToken ct = default)
    {
        return await _db.ScanTasks
            .AsNoTracking()
            .Select(x => x.TaskId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> NonTerminalTaskIdsAsync(CancellationToken ct = default)
    {
        return await _db.ScanTasks
            .AsNoTracking()
            .Where(x => x.Status != "Completed" && x.Status != "Failed" && x.Status != "Aborted")
            .Select(x => x.TaskId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> RecoverAfterServiceRestartAsync(CancellationToken ct = default)
    {
        var ids = await NonTerminalTaskIdsAsync(ct).ConfigureAwait(false);
        var recovered = 0;
        foreach (var id in ids)
        {
            var applied = false;
            await UpdateAsync(id, doc =>
            {
                if (doc.Terminal)
                {
                    return;
                }

                doc.Status = "Failed";
                doc.Phase = "Interrupted";
                doc.Result = null;
                doc.Error = new TaskApiError
                {
                    Code = "SERVICE_RESTARTED",
                    Message = "服務曾重新啟動，未完成的硬體任務不能自動重播，請重新開始。",
                };
                applied = true;
            }, ct).ConfigureAwait(false);
            if (applied)
            {
                recovered++;
            }
        }

        return recovered;
    }

    /// <inheritdoc />
    public TaskEnvelope Envelope(TaskDocument document)
    {
        return new TaskEnvelope
        {
            TaskId = document.TaskId,
            Status = document.Status,
            Result = document.Status == "Completed" ? document.Result : null,
            Error = document.Status == "Failed" ? document.Error : null,
            TraceId = document.TraceId,
            Phase = document.Phase,
            IsReplay = document.IsReplay,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            Callback = new TaskCallbackEnvelope
            {
                Mode = document.CallbackUrl is null ? "NotRequested" : CallbackMode,
                CallbackDelivered = document.CallbackDelivered,
                CallbackAttempts = document.CallbackAttempts,
                CallbackLastError = document.CallbackLastError,
            },
        };
    }

    /// <summary>檢查 taskId 字元。</summary>
    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
        {
            throw new ScanTaskException(400, "INVALID_TASK_ID", "taskId 格式不正確。");
        }
    }

    /// <summary>依種類組 404。</summary>
    private static ScanTaskException Missing(string? kind)
    {
        var code = kind == "Surface"
            ? "SURF_001"
            : kind == "Calibration"
                ? "CAL_001"
                : kind == "Line"
                    ? "SCAN_LINE_001"
                    : "TASK_NOT_FOUND";
        return new ScanTaskException(404, code, "任務不存在或已過保存期限。");
    }

    /// <summary>task.json 路徑。</summary>
    private string TaskPath(string id)
    {
        return Path.Combine(TaskDirectory(id), "task.json");
    }

    /// <summary>檔或列是否已存在。</summary>
    private async Task<bool> ExistsUnlockedAsync(string id, CancellationToken ct)
    {
        if (File.Exists(TaskPath(id)))
        {
            return true;
        }

        return await _db.ScanTasks.AsNoTracking().AnyAsync(x => x.TaskId == id, ct).ConfigureAwait(false);
    }

    /// <summary>優先讀 task.json，沒有再讀 DocumentJson。</summary>
    private async Task<TaskDocument?> ReadUnlockedAsync(string id, CancellationToken ct)
    {
        var path = TaskPath(id);
        if (File.Exists(path))
        {
            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<TaskDocument>(text, JsonDefaults.Options);
        }

        var row = await _db.ScanTasks.AsNoTracking().FirstOrDefaultAsync(x => x.TaskId == id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<TaskDocument>(row.DocumentJson, JsonDefaults.Options);
    }

    /// <summary>寫入 DB 與原子 task.json。</summary>
    private async Task PersistUnlockedAsync(TaskDocument doc, bool insert, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(doc, JsonDefaults.Options);
        if (insert)
        {
            _db.ScanTasks.Add(MapEntity(doc, json));
        }
        else
        {
            var row = await _db.ScanTasks.FirstOrDefaultAsync(x => x.TaskId == doc.TaskId, ct).ConfigureAwait(false);
            if (row is null)
            {
                _db.ScanTasks.Add(MapEntity(doc, json));
            }
            else
            {
                ApplyEntity(row, doc, json);
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await WriteAtomicAsync(TaskPath(doc.TaskId), json).ConfigureAwait(false);
    }

    /// <summary>原子覆寫 JSON 檔（先寫暫存再 Move）。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.TaskRepository</c>。</remarks>
    private async Task WriteAtomicAsync(string path, string json)
    {
        var fullPath = Path.GetFullPath(path);
        var rootPrefix = Root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage path must remain under the configured data directory.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(temp, fullPath, overwrite: true);
                    break;
                }
                catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(20 * (1 << attempt)).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>新建列。</summary>
    private static ScanTaskEntity MapEntity(TaskDocument doc, string json)
    {
        var row = new ScanTaskEntity();
        ApplyEntity(row, doc, json);
        return row;
    }

    /// <summary>同步索引欄與 DocumentJson。</summary>
    private static void ApplyEntity(ScanTaskEntity row, TaskDocument doc, string json)
    {
        row.TaskId = doc.TaskId;
        row.Kind = doc.Kind;
        row.Status = doc.Status;
        row.Phase = doc.Phase;
        row.ScannerId = doc.ScannerId;
        row.JobId = doc.JobId;
        row.DocumentJson = json;
        row.CreatedAt = doc.CreatedAt;
        row.UpdatedAt = doc.UpdatedAt;
        row.CallbackUrl = doc.CallbackUrl;
        row.CallbackDelivered = doc.CallbackDelivered;
        row.CallbackAttempts = doc.CallbackAttempts;
        row.CallbackLastError = doc.CallbackLastError;
        row.CallbackNextAttemptAt = doc.CallbackNextAttemptAt;
    }
}
