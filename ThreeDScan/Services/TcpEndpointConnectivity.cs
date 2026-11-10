using System.Net.Sockets;

namespace ThreeDScan.Services;

/// <summary>TCP 連線探測（不送協定資料）。</summary>
public static class TcpEndpointConnectivity
{
    /// <summary>嘗試連上指定 IP 與埠。</summary>
    /// <param name="ip">主機位址。</param>
    /// <param name="port">埠號。</param>
    /// <param name="timeoutMs">逾時毫秒。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>連線成功且可關閉時為 true。</returns>
    public static async Task<bool> TryConnectAsync(string ip, int port, int timeoutMs, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ip) || port <= 0 || port > 65535)
        {
            return false;
        }

        try
        {
            using var client = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeoutMs);
            await client.ConnectAsync(ip.Trim(), port, linked.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or TimeoutException)
        {
            return false;
        }
    }
}
