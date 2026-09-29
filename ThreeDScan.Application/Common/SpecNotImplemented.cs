using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Common;

/// <summary>尚未實作的 API 統一回傳；HTTP 建議 501。</summary>
public static class SpecNotImplemented
{
    /// <summary>業務邏輯尚未實作。</summary>
    public const string ErrorCode = "SYS_NYI";

    /// <summary>產生尚未實作的失敗包裝。</summary>
    public static ApiResponse<T> Response<T>(string endpoint)
    {
        return ApiResponse<T>.Fail(ErrorCode, $"{endpoint} 尚未實作。");
    }
}
