using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Scan;

/// <summary>將任務文件轉成 GET／callback 共用之 <see cref="TaskResultData"/>。</summary>
public static class TaskResultDataMapper
{
    /// <summary>依任務終態組出指南五欄；邏輯與 GET /api/tasks/{taskId}/result 一致。</summary>
    /// <param name="document">已持久化之任務。</param>
    /// <returns>對外任務結果。</returns>
    public static TaskResultData FromDocument(TaskDocument document)
    {
        object? result = null;
        TaskErrorDto? error = null;
        if (document.Status == TaskResultData.StatusCompleted)
        {
            result = document.Result;
        }
        else if (document.Status == TaskResultData.StatusFailed && document.Error is not null)
        {
            error = new TaskErrorDto
            {
                Code = document.Error.Code,
                Message = document.Error.Message,
                Details = document.Error.Details,
            };
        }

        return new TaskResultData
        {
            TaskId = document.TaskId,
            Status = document.Status,
            Result = result,
            Error = error,
            TraceId = document.TraceId,
        };
    }
}
