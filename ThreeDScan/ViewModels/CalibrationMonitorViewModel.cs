using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using ThreeDScan.Common;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Measure;

namespace ThreeDScan.ViewModels;

/// <summary>訂閱 Api 儀器校正結果；3D 顯示對齊 LJScanWinform UpdateViewport。</summary>
public class CalibrationMonitorViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly LjCalibrationParameters _calibrationParams;
    private readonly string _storageContentsRoot;
    private readonly bool _isDebugMode;

    private HubConnection? _hub;
    private string _apiBaseUrl = "http://localhost:5295";
    private string _connectionState = "未連線";
    private string _lastScannerId = "";
    private string _statusText = "等待外部呼叫 POST /api/calibration/instrument";
    private string _detailText = "";
    private string _viewportStatus = "（等待儀器校正 API 回傳影像）";

    public CalibrationMonitorViewModel(
        LjCalibrationParameters calibrationParams,
        string storageContentsRoot,
        bool isDebugMode = false)
    {
        _calibrationParams = calibrationParams;
        _storageContentsRoot = storageContentsRoot;
        _isDebugMode = isDebugMode;
    }

    /// <summary>與 Api <c>Calibration:ResponseMode</c> 對齊；Debug 才顯示球清單與 3D。</summary>
    public bool IsDebugMode => _isDebugMode;

    public Visibility DebugPanelVisibility => _isDebugMode ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<BallRow> Balls { get; } = new();

    public event EventHandler<LjViewportUpdateEventArgs>? ViewportUpdated;

    public string ConnectionState
    {
        get => _connectionState;
        set => SetProperty(ref _connectionState, value);
    }

    public string LastScannerId
    {
        get => _lastScannerId;
        set => SetProperty(ref _lastScannerId, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string DetailText
    {
        get => _detailText;
        set => SetProperty(ref _detailText, value);
    }

    public string ViewportStatus
    {
        get => _viewportStatus;
        set => SetProperty(ref _viewportStatus, value);
    }

    public async Task ConnectAsync(string hubUrl, string? apiBaseUrl = null)
    {
        if (_hub is not null) return;

        if (!string.IsNullOrWhiteSpace(apiBaseUrl))
            _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        else
            _apiBaseUrl = DeriveApiBaseUrl(hubUrl);

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hub.On<CalibrationPreviewMessage>(CalibrationPreviewHubEvent, OnPreview);

        _hub.Reconnecting += _ =>
        {
            ConnectionState = "重新連線中…";
            return Task.CompletedTask;
        };
        _hub.Reconnected += _ =>
        {
            ConnectionState = "已連線";
            return Task.CompletedTask;
        };
        _hub.Closed += _ =>
        {
            ConnectionState = "已斷線";
            return Task.CompletedTask;
        };

        await _hub.StartAsync();
        ConnectionState = "已連線";
    }

    private void OnPreview(CalibrationPreviewMessage msg)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            LastScannerId = msg.ScannerId;
            StatusText = msg.Success
                ? $"[{msg.ScannerId}] 儀器校正成功 · {msg.Timestamp:HH:mm:ss}"
                : $"[{msg.ScannerId}] 儀器校正失敗 · {msg.ErrorMessage}";

            Balls.Clear();
            if (_isDebugMode && msg.Balls is not null)
            {
                foreach (var b in msg.Balls.Where(b => b.IsWithinTolerance))
                {
                    Balls.Add(new BallRow
                    {
                        Index = b.Index,
                        Line = $"球[{b.Index}] X={b.X:F2} Y={b.Y:F2} Z={b.Z:F2} D={b.Diameter:F3} ✓",
                    });
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"TraceId: {msg.TraceId}");
            if (msg.CompensationOffsetX is double ox)
                sb.AppendLine($"補償: X={ox:F3} Y={msg.CompensationOffsetY:F3} Z={msg.CompensationOffsetZ:F3}");
            if (msg.Rmse is double rmse)
                sb.AppendLine($"RMSE: {rmse:F4}");
            if (_isDebugMode && msg.MatchedBalls is { Count: > 0 } matched)
            {
                sb.AppendLine("配對（known vs measured）：");
                foreach (var m in matched)
                {
                    sb.AppendLine(
                        $"  {m.BallId}: known({m.KnownX:F1},{m.KnownY:F1}) " +
                        $"meas({m.MeasuredX:F1},{m.MeasuredY:F1}) res={m.ResidualMm:F2}mm");
                }
            }

            if (!string.IsNullOrWhiteSpace(msg.ErrorMessage))
                sb.AppendLine(msg.ErrorMessage);
            if (!_isDebugMode)
                sb.AppendLine("（Production：僅補償摘要；本機 Debug 建置會自動載入 appsettings.Development.json）");
            DetailText = sb.ToString().TrimEnd();

            if (!_isDebugMode)
                ViewportStatus = "正式模式不顯示 3D 預覽。";
        });

        if (_isDebugMode)
            _ = Task.Run(() => TryUpdateViewport(msg));
    }

    private void TryUpdateViewport(CalibrationPreviewMessage msg)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(msg.PreviewHeightRelativePath)
                || string.IsNullOrWhiteSpace(msg.PreviewGrayRelativePath)
                || string.IsNullOrWhiteSpace(msg.ContentsFolder))
            {
                SetViewportStatus("缺少 3D 預覽影像路徑。");
                return;
            }

            var heightPath = ResolvePreviewPath(msg.PreviewHeightRelativePath);
            var grayPath = ResolvePreviewPath(msg.PreviewGrayRelativePath);
            if (!File.Exists(heightPath) || !File.Exists(grayPath))
            {
                SetViewportStatus($"找不到預覽影像：\n{heightPath}");
                return;
            }

            var config = new ScannerConfigRecord
            {
                ScannerId = msg.ScannerId,
                RefPatternX = msg.RefPatternX ?? 0,
                RefPatternY = msg.RefPatternY ?? 0,
                RefSphereX = msg.RefSphereX ?? 0,
                RefSphereY = msg.RefSphereY ?? 0,
                ContentsFolder = msg.ContentsFolder,
                UpdatedAt = DateTimeOffset.Now,
            };

            var viewportParams = BuildViewportParams(msg);
            var result = LjViewportMeasureRunner.MeasureForViewport(
                msg.ContentsFolder,
                heightPath,
                grayPath,
                config,
                viewportParams);

            SetViewportStatus("");
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                ViewportUpdated?.Invoke(this, new LjViewportUpdateEventArgs { Result = result }));
        }
        catch (Exception ex)
        {
            SetViewportStatus($"3D 預覽失敗：{ex.Message}");
        }
    }

    private string ResolvePreviewPath(string relativePath)
    {
        var rel = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(rel)
            ? rel
            : Path.Combine(_storageContentsRoot, rel);
    }

    private void SetViewportStatus(string text)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => ViewportStatus = text);
    }

    /// <summary>3D 預覽沿用 Api 本次量測的 DB 合併參數，避免 WPF appsettings 仍為 24.9。</summary>
    private LjCalibrationParameters BuildViewportParams(CalibrationPreviewMessage msg)
    {
        if (msg.ExpectedBallDiameter is not double expected)
            return _calibrationParams;

        return new LjCalibrationParameters
        {
            ExpectedBallDiameter = expected,
            MaxDiameterError = msg.MaxDiameterError ?? _calibrationParams.MaxDiameterError,
            DiameterTolerance = msg.DiameterTolerance ?? _calibrationParams.DiameterTolerance,
            BallCount = msg.DetectCount ?? _calibrationParams.BallCount,
            DatumPlane = _calibrationParams.DatumPlane,
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }

    private static string DeriveApiBaseUrl(string hubUrl)
    {
        const string suffix = "/hubs/calibration-preview";
        if (hubUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return hubUrl[..^suffix.Length].TrimEnd('/');
        return hubUrl.TrimEnd('/');
    }

    private const string CalibrationPreviewHubEvent = "CalibrationPreview";

    public sealed class BallRow
    {
        public int Index { get; init; }
        public string Line { get; init; } = "";
    }

    public sealed class LjViewportUpdateEventArgs : EventArgs
    {
        public required LjViewportMeasureResult Result { get; init; }
    }

    public sealed class CalibrationPreviewMessage
    {
        public string ScannerId { get; set; } = "";
        public string TraceId { get; set; } = "";
        public DateTimeOffset Timestamp { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public IReadOnlyList<BallDto>? Balls { get; set; }
        public double? CompensationOffsetX { get; set; }
        public double? CompensationOffsetY { get; set; }
        public double? CompensationOffsetZ { get; set; }
        public double? Rmse { get; set; }
        public string? PreviewGrayRelativePath { get; set; }
        public string? PreviewHeightRelativePath { get; set; }
        public string? ContentsFolder { get; set; }
        public double? RefPatternX { get; set; }
        public double? RefPatternY { get; set; }
        public double? RefSphereX { get; set; }
        public double? RefSphereY { get; set; }
        public double? ExpectedBallDiameter { get; set; }
        public double? MaxDiameterError { get; set; }
        public double? DiameterTolerance { get; set; }
        public int? DetectCount { get; set; }
        public IReadOnlyList<MatchedBallDto>? MatchedBalls { get; set; }
    }

    public sealed class MatchedBallDto
    {
        public string BallId { get; set; } = "";
        public double KnownX { get; set; }
        public double KnownY { get; set; }
        public double MeasuredX { get; set; }
        public double MeasuredY { get; set; }
        public double ResidualMm { get; set; }
    }

    public sealed class BallDto
    {
        public int Index { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Diameter { get; set; }
        public bool IsWithinTolerance { get; set; }
    }
}
