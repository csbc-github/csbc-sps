using System.Text.Json;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>段產物 JSON 原子寫入（Hank TaskRepository.WriteAtomicAsync）。</summary>
public static class SegmentFileAtomicWrite
{
    /// <summary>暫存檔寫入後以 Move 替換目標檔。</summary>
    /// <typeparam name="T">序列化型別。</typeparam>
    /// <param name="path">目標路徑。</param>
    /// <param name="value">內容。</param>
    /// <param name="ct">取消權杖。</param>
    public static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(value, JsonDefaults.Options);
            await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
            for (var attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    File.Move(temp, fullPath, overwrite: true);
                    break;
                }
                catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(20 * (1 << attempt), ct).ConfigureAwait(false);
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
}
