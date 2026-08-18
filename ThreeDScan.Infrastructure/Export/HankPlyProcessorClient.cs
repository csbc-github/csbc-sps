using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ThreeDScan.Infrastructure.Configuration;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// 對齊 Hank <c>PythonBackendClient</c>（code-V2_260716）：
/// POST /process → GET /status SSE。
/// 額外：略過 SSE 重播的舊 done，並要求輸出檔 mtime ≥ 本次輸入（文件 §9.3/§9.4）。
/// </summary>
public sealed class HankPlyProcessorClient
{
    private readonly string _rootDir;
    private readonly Uri _baseUri;
    private readonly float _zThresh;
    private readonly float _cell;
    private Process? _ownedProcess;

    public HankPlyProcessorClient(PlyProcessorOptions options)
    {
        _rootDir = options.ResolveRootPath();
        if (!Directory.Exists(_rootDir))
            throw new DirectoryNotFoundException(
                $"找不到內建 ply_processor 目錄：{_rootDir}。請確認已建置並複製 ThirdParty/ply_processor。");

        _baseUri = new Uri($"http://127.0.0.1:{options.Port}/");
        _zThresh = options.ZThresh;
        _cell = options.Cell;
    }

    public string ScansDirectory => Path.Combine(_rootDir, "scans");

    public async Task<string> ProcessAsync(string plyPath, Action<string>? log = null, CancellationToken ct = default)
    {
        log ??= static _ => { };
        await EnsureRunningAsync(log, ct).ConfigureAwait(false);

        Directory.CreateDirectory(ScansDirectory);
        var fileName = Path.GetFileName(plyPath);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var stagedPath = Path.Combine(ScansDirectory, fileName);
        if (!string.Equals(Path.GetFullPath(plyPath), Path.GetFullPath(stagedPath), StringComparison.OrdinalIgnoreCase))
            File.Copy(plyPath, stagedPath, overwrite: true);

        var expectedOut = Path.Combine(ScansDirectory, $"{baseName}_angle_steel.ply");
        var inputWriteTimeUtc = File.GetLastWriteTimeUtc(stagedPath);

        // 文件 §3.5：既有輸出且不早於輸入 → 重用（時間必須真的新）
        var existing = FindExistingProcessedOutput(stagedPath);
        if (existing != null)
        {
            log($"使用 Python 既有輸出：{Path.GetFileName(existing)}");
            return existing;
        }

        // 避免 /status 重播舊 done 時複製到昨天的同名檔
        TryDelete(expectedOut);

        log("呼叫 Python /process...");
        await PostProcessAsync(stagedPath, fileName, ct).ConfigureAwait(false);

        log("等待 Python 處理完成...");
        using var http = new HttpClient { BaseAddress = _baseUri, Timeout = TimeSpan.FromMinutes(30) };
        var output = await WaitForFreshOutputAsync(http, expectedOut, inputWriteTimeUtc, log, ct)
            .ConfigureAwait(false);
        var local = ResolveOutputPath(output);
        if (!File.Exists(local))
            throw new FileNotFoundException($"Python 已回傳輸出，但找不到檔案：{local}", local);
        if (File.GetLastWriteTimeUtc(local) < inputWriteTimeUtc)
            throw new InvalidOperationException($"輸出檔不是本次處理結果：{local}");

        log($"Python 輸出：{Path.GetFileName(local)}");
        return local;
    }

    Task PostProcessAsync(string plyPath, string fileName, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var boundary = "----PlyProcessorBoundary" + Guid.NewGuid().ToString("N");
            var url = new Uri(_baseUri, "process");
#pragma warning disable SYSLIB0014
            var request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
            request.Method = "POST";
            request.ProtocolVersion = HttpVersion.Version10;
            request.KeepAlive = false;
            request.SendChunked = false;
            request.AllowWriteStreamBuffering = false;
            request.Timeout = (int)TimeSpan.FromMinutes(30).TotalMilliseconds;
            request.ReadWriteTimeout = (int)TimeSpan.FromMinutes(30).TotalMilliseconds;
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            request.UserAgent = "ThreeDScan";
            request.ServicePoint.Expect100Continue = false;

            var z = _zThresh.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var cell = _cell.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var fileHeader =
                $"--{boundary}\r\n" +
                $"Content-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\n" +
                "Content-Type: application/octet-stream\r\n\r\n";
            var fields =
                $"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"filename\"\r\n\r\n{fileName}" +
                $"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"z_thresh\"\r\n\r\n{z}" +
                $"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"cell\"\r\n\r\n{cell}" +
                $"\r\n--{boundary}--\r\n";

            var headerBytes = Encoding.UTF8.GetBytes(fileHeader);
            var fieldBytes = Encoding.UTF8.GetBytes(fields);
            var fileInfo = new FileInfo(plyPath);
            request.ContentLength = headerBytes.Length + fileInfo.Length + fieldBytes.Length;

            using (var reqStream = request.GetRequestStream())
            {
                reqStream.Write(headerBytes, 0, headerBytes.Length);
                using var fs = File.OpenRead(plyPath);
                fs.CopyTo(reqStream);
                reqStream.Write(fieldBytes, 0, fieldBytes.Length);
            }

            try
            {
                using var response = (HttpWebResponse)request.GetResponse();
                using var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null);
                var body = reader.ReadToEnd();
                if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                    throw new HttpRequestException($"Python /process HTTP {(int)response.StatusCode}: {body}");
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse response)
            {
                using var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null);
                var body = reader.ReadToEnd();
                throw new HttpRequestException($"Python /process HTTP {(int)response.StatusCode}: {body}", ex);
            }
        }, ct);
    }

    async Task EnsureRunningAsync(Action<string> log, CancellationToken ct)
    {
        var exe = Path.Combine(_rootDir, "ply_processor_fixed.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException("找不到 Python 打包程式 ply_processor_fixed.exe。", exe);

        Directory.CreateDirectory(ScansDirectory);

        if (FindBundledProcess() != null)
        {
            log("使用已啟動的 Python backend。");
            await WaitUntilReadyAsync(ct).ConfigureAwait(false);
            return;
        }

        // 其他目錄實例占用 8000 時會寫到別的 scans（文件 §9.2）
        if (IsServerReady() || FindAnyPlyProcessor() != null)
        {
            log("關閉占用中的 ply_processor…");
            KillAllPlyProcessors();
            for (var i = 0; i < 20 && IsServerReady(); i++)
                await Task.Delay(250, ct).ConfigureAwait(false);
        }

        log("啟動 Python backend...");
        _ownedProcess = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = _rootDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"--port {_baseUri.Port} --work-dir \"{ScansDirectory}\"",
        }) ?? throw new InvalidOperationException($"無法啟動 {exe}");

        await WaitUntilReadyAsync(ct).ConfigureAwait(false);
    }

    Process? FindBundledProcess()
    {
        foreach (var name in new[] { "ply_processor_fixed", "ply_processor" })
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = proc.MainModule?.FileName;
                    if (path != null && path.StartsWith(_rootDir, StringComparison.OrdinalIgnoreCase))
                        return proc;
                }
                catch { }
            }
        }

        return null;
    }

    static Process? FindAnyPlyProcessor()
    {
        foreach (var name in new[] { "ply_processor_fixed", "ply_processor" })
        {
            var list = Process.GetProcessesByName(name);
            if (list.Length > 0) return list[0];
        }

        return null;
    }

    static void KillAllPlyProcessors()
    {
        foreach (var name in new[] { "ply_processor_fixed", "ply_processor" })
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(5000);
                }
                catch { }
            }
        }
    }

    bool IsServerReady()
    {
        try
        {
            using var probe = new HttpClient { BaseAddress = _baseUri, Timeout = TimeSpan.FromSeconds(1) };
            using var resp = probe.GetAsync("").GetAwaiter().GetResult();
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    async Task WaitUntilReadyAsync(CancellationToken ct)
    {
        for (var i = 0; i < 40; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (IsServerReady()) return;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException("Python backend 啟動逾時。若 8000 port 被占用，請先關閉它。");
    }

    async Task<string> WaitForFreshOutputAsync(
        HttpClient http,
        string expectedOut,
        DateTime minAcceptUtc,
        Action<string> log,
        CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromMinutes(15));

        var diskTask = Task.Run(async () =>
        {
            while (!linked.IsCancellationRequested)
            {
                if (IsFresh(expectedOut, minAcceptUtc))
                    return expectedOut;
                await Task.Delay(400, linked.Token).ConfigureAwait(false);
            }

            throw new OperationCanceledException(linked.Token);
        }, linked.Token);

        var statusTask = Task.Run(async () =>
        {
            using var stream = await http.GetStreamAsync("status", linked.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            string? preferred = null;
            string? fallback = null;
            var staleDone = 0;

            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(linked.Token).ConfigureAwait(false);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:")) continue;

                using var doc = JsonDocument.Parse(line[5..].Trim());
                var root = doc.RootElement;
                if (root.TryGetProperty("log", out var logEl))
                {
                    var msg = logEl.GetString() ?? "";
                    log(msg);
                    var written = TryParseWrittenPly(msg);
                    if (written != null)
                    {
                        if (IsAngleSteelOutput(written)) preferred = written;
                        else if (!IsDoorStopperOutput(written)) fallback = written;
                    }
                }

                if (!root.TryGetProperty("done", out var doneEl) || !doneEl.GetBoolean())
                    continue;
                if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind != JsonValueKind.Null)
                    throw new InvalidOperationException(errEl.GetString());

                string? candidate = null;
                if (root.TryGetProperty("outputs", out var outputs))
                {
                    var plyOutputs = new List<string>();
                    foreach (var item in outputs.EnumerateArray())
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value) && value.EndsWith(".ply", StringComparison.OrdinalIgnoreCase))
                            plyOutputs.Add(value);
                    }

                    candidate = plyOutputs.FirstOrDefault(IsAngleSteelOutput)
                        ?? plyOutputs.FirstOrDefault(x => !IsDoorStopperOutput(x));
                }

                candidate ??= preferred ?? fallback ?? expectedOut;
                var local = ResolveOutputPath(candidate);

                // 重播的舊 done：檔案尚未更新 → 略過並繼續等
                for (var i = 0; i < 15; i++)
                {
                    if (IsFresh(local, minAcceptUtc))
                        return local;
                    await Task.Delay(200, linked.Token).ConfigureAwait(false);
                }

                staleDone++;
                log($"略過 /status 舊 done（#{staleDone}），繼續等待新輸出…");
            }

            throw new IOException("Python /status 串流中斷。");
        }, linked.Token);

        var winner = await Task.WhenAny(statusTask, diskTask).ConfigureAwait(false);
        try
        {
            var result = await winner.ConfigureAwait(false);
            linked.Cancel();
            return result;
        }
        catch
        {
            var other = ReferenceEquals(winner, statusTask) ? diskTask : statusTask;
            return await other.ConfigureAwait(false);
        }
    }

    string? FindExistingProcessedOutput(string plyPath)
    {
        var baseName = Path.GetFileNameWithoutExtension(plyPath);
        var inputTime = File.GetLastWriteTime(plyPath);
        foreach (var candidate in new[]
                 {
                     Path.Combine(ScansDirectory, $"{baseName}_angle_steel.ply"),
                     Path.Combine(ScansDirectory, $"{baseName}_obj1.ply"),
                 })
        {
            if (File.Exists(candidate) && File.GetLastWriteTime(candidate) >= inputTime)
                return candidate;
        }

        return null;
    }

    static bool IsFresh(string path, DateTime minAcceptUtc)
        => File.Exists(path) && File.GetLastWriteTimeUtc(path) >= minAcceptUtc;

    string ResolveOutputPath(string output)
    {
        if (Path.IsPathRooted(output) && File.Exists(output))
            return output;
        var name = Path.GetFileName(output.Replace('\\', Path.DirectorySeparatorChar));
        return Path.Combine(ScansDirectory, name);
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    static string? TryParseWrittenPly(string msg)
    {
        const string marker = "writing ";
        var idx = msg.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var path = msg[(idx + marker.Length)..].Trim();
        return path.EndsWith(".ply", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    static bool IsAngleSteelOutput(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains("_angle_steel", StringComparison.OrdinalIgnoreCase)
            || name.Equals("angle_steel.ply", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsDoorStopperOutput(string path)
        => Path.GetFileName(path).Contains("door_stopper", StringComparison.OrdinalIgnoreCase);

    public static int CountPlyVertices(string plyPath)
    {
        using var reader = new StreamReader(plyPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("element vertex ", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line["element vertex ".Length..].Trim(), out var n))
                return n;
            if (line.Equals("end_header", StringComparison.OrdinalIgnoreCase))
                break;
        }

        return 0;
    }
}
