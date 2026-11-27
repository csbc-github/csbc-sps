using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Media3D;
using Microsoft.Extensions.Configuration;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Common;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Scan;
using ThreeDScan.Models;
using ThreeDScan.Services;

namespace ThreeDScan.ViewModels;

/// <summary>研磨區：逐孔顯示實測邊界、封閉狀態與同座標系的點雲標記。</summary>
/// <remarks>
/// 點雲來自監看檔 <c>rawPlyPath</c>（掃描器座標）；
/// <see cref="PathPoints"/> 為公開絕對座標；3D 標記只使用各孔獨立保存的原始掃描器座標。
/// </remarks>
public class GrindingViewModel : ViewModelBase
{
    private FeaturePathPoint? _selectedPathPoint;
    private FileSystemWatcher? _watch;
    private int _watchVersion;
    private readonly string _contentsRoot;
    private readonly IHoleBoundaryReader _boundary;
    private IReadOnlyList<PointCloudRenderVertex> _pointCloudVertices = Array.Empty<PointCloudRenderVertex>();
    private bool _hasPointCloud;
    private HoleFeatureViewItem? _selectedFeature;
    private IReadOnlyList<Point3D> _scannerBoundaryPoints = Array.Empty<Point3D>();
    private IReadOnlyList<ThreeDScan.Application.Common.Models.CoordinateDto> _legacyPoints =
        Array.Empty<ThreeDScan.Application.Common.Models.CoordinateDto>();
    private bool _hasSnapshot;
    private string? _taskId;
    private string _coordinateStatus = "尚無座標資料";
    private string _measurementSourceCaption = "尚未載入量測資料";
    private string _measurementSourceTooltip = "";
    private string _scopeStatus = "";

    public string ScopeStatus
    {
        get => _scopeStatus;
        private set => SetProperty(ref _scopeStatus, value);
    }

    public string MeasurementSourceCaption
    {
        get => _measurementSourceCaption;
        private set => SetProperty(ref _measurementSourceCaption, value);
    }

    public string MeasurementSourceTooltip
    {
        get => _measurementSourceTooltip;
        private set => SetProperty(ref _measurementSourceTooltip, value);
    }

    public string CoordinateStatus
    {
        get => _coordinateStatus;
        private set => SetProperty(ref _coordinateStatus, value);
    }

    public ObservableCollection<HoleFeatureViewItem> Features { get; } = new();
    public int ClosedFeatureCount => Features.Count(f => f.Closed);
    public int PartialFeatureCount => Features.Count(f => !f.Closed);
    public bool HasFeatureSelections => Features.Count > 0;
    public string HoleSummary => _hasSnapshot
        ? $"封閉孔 {ClosedFeatureCount} 個 · 部分開口 {PartialFeatureCount} 個"
        : "等待孔洞辨識結果";
    public string SelectedFeatureStatus => SelectedFeature?.Description
        ?? (_legacyPoints.Count > 0 ? "舊版量測邊界；尚無各孔封閉狀態與掃描器孔緣標記。"
            : _hasSnapshot ? "本次未觀察到可解析開口；請核對掃描範圍及遮擋。" : "尚無孔緣資料。");
    public bool SelectedFeatureIsClosed => SelectedFeature?.Closed == true;
    public IReadOnlyList<Point3D> ScannerBoundaryPoints
    {
        get => _scannerBoundaryPoints;
        private set => SetProperty(ref _scannerBoundaryPoints, value);
    }
    public HoleFeatureViewItem? SelectedFeature
    {
        get => _selectedFeature;
        set
        {
            if (SetProperty(ref _selectedFeature, value)) ApplySelection();
        }
    }

    /// <summary>邊界座標序列。</summary>
    public ObservableCollection<FeaturePathPoint> PathPoints { get; } = new();

    /// <summary>WPF 3D 點雲頂點（掃描器座標）。</summary>
    public IReadOnlyList<PointCloudRenderVertex> PointCloudVertices
    {
        get
        {
            return _pointCloudVertices;
        }
        private set
        {
            if (SetProperty(ref _pointCloudVertices, value))
            {
                HasPointCloud = value.Count > 0;
            }
        }
    }

    /// <summary>是否有可顯示的點雲。</summary>
    public bool HasPointCloud
    {
        get
        {
            return _hasPointCloud;
        }
        private set
        {
            SetProperty(ref _hasPointCloud, value);
        }
    }

    /// <summary>目前選取節點。</summary>
    public FeaturePathPoint? SelectedPathPoint
    {
        get
        {
            return _selectedPathPoint;
        }
        set
        {
            SetProperty(ref _selectedPathPoint, value);
        }
    }

    /// <summary>建立研磨區 VM，監看孔洞邊界檔。</summary>
    public GrindingViewModel()
        : this(AppConfiguration.Load(), new HoleBoundaryReader())
    {
    }

    /// <summary>指定設定來源。</summary>
    /// <param name="config">設定。</param>
    public GrindingViewModel(IConfiguration config)
        : this(config, new HoleBoundaryReader())
    {
    }

    /// <summary>指定設定與邊界讀取。</summary>
    /// <param name="config">設定。</param>
    /// <param name="boundary">監看檔讀取。</param>
    public GrindingViewModel(IConfiguration config, IHoleBoundaryReader boundary)
    {
        _contentsRoot = config["Storage:ContentsRoot"] ?? "";
        _boundary = boundary;
        StartWatching();
        QueueReload();
    }

    /// <summary>監看 ContentsRoot 下的孔洞邊界檔變更。</summary>
    private void StartWatching()
    {
        var path = ScanMonitorPaths.HoleBoundaryFile(_contentsRoot);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        _watch = new FileSystemWatcher(dir, ScanMonitorPaths.HoleBoundaryFileName)
        {
            NotifyFilter = NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.CreationTime
                | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watch.Created += (_, _) => QueueReload();
        _watch.Changed += (_, _) => QueueReload();
    }

    /// <summary>合併連續寫檔事件後再重載。</summary>
    private void QueueReload()
    {
        var version = Interlocked.Increment(ref _watchVersion);
        _ = ReloadAfterDelayAsync(version);
    }

    /// <summary>延遲後把監看檔結果綁到清單與點雲。</summary>
    /// <param name="version">這次重載序號；過期則略過。</param>
    private async Task ReloadAfterDelayAsync(int version)
    {
        await Task.Delay(200).ConfigureAwait(true);
        if (version != _watchVersion)
        {
            return;
        }

        await ApplyBoundaryAsync(version).ConfigureAwait(true);
    }

    /// <summary>讀邊界並更新 <see cref="PathPoints"/> 與點雲。</summary>
    /// <param name="version">重載序號。</param>
    private async Task ApplyBoundaryAsync(int version)
    {
        var snapshot = _boundary.TryRead(_contentsRoot);
        if (snapshot is null)
        {
            return;
        }

        // Display data and overlays both retain native scanner XYZ. Absolute
        // coordinates are kept exclusively in the per-hole table.
        IReadOnlyList<PointCloudRenderVertex> verts = Array.Empty<PointCloudRenderVertex>();
        string? displayPly = !string.IsNullOrWhiteSpace(snapshot.ExtractedPlyPath) && File.Exists(snapshot.ExtractedPlyPath)
            ? snapshot.ExtractedPlyPath : snapshot.RawPlyPath;
        if (!string.IsNullOrWhiteSpace(displayPly) && File.Exists(displayPly))
        {
            try
            {
                verts = await AsciiPlyPointCloudDisplayLoader.LoadAsync(displayPly, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                verts = Array.Empty<PointCloudRenderVertex>();
            }
        }

        if (version != _watchVersion)
        {
            return;
        }

        void Apply()
        {
            string? previousId = _taskId is not null && _taskId == snapshot.TaskId ? SelectedFeature?.Id : null;
            _taskId = snapshot.TaskId;
            _legacyPoints = snapshot.Features.Count == 0 ? snapshot.Points : [];
            _hasSnapshot = true;
            string source = snapshot.TaskId is not { Length: > 0 } taskId ? "舊版量測資料"
                : taskId.StartsWith("DIAGNOSTIC-HOLE", StringComparison.OrdinalIgnoreCase) ? "實機驗證掃描"
                : $"量測 {(taskId.Length > 24 ? taskId[..24] + "…" : taskId)}";
            string time = snapshot.CapturedAt?.ToString("MM/dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "時間未提供";
            MeasurementSourceCaption = $"{source} · {time}";
            MeasurementSourceTooltip = $"任務：{snapshot.TaskId ?? "未提供"}\n拍攝時間：{snapshot.CapturedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "未提供"}";
            ScopeStatus = !snapshot.AngleSteelOnly ? "原始掃描中的全部量測開口"
                : snapshot.WorkpieceIdentified ? "僅記錄角鋼翼板上的孔洞"
                : "本次未確認可用的角鋼板面，不記錄背景開口。";
            CoordinateStatus = snapshot.AbsoluteCoordinatesCalibrated switch
            {
                true => "絕對座標：已完成外參驗證",
                false => "絕對座標：依設定換算，尚未完成外參驗證",
                null => "絕對座標：此筆資料未提供外參驗證狀態",
            };
            Features.Clear();
            foreach (var feature in snapshot.Features) Features.Add(new HoleFeatureViewItem(feature));
            _selectedFeature = Features.FirstOrDefault(f => f.Id == previousId)
                ?? Features.FirstOrDefault(f => f.Id == snapshot.SelectedFeatureId)
                ?? Features.FirstOrDefault(f => f.Closed) ?? Features.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedFeature));
            OnPropertyChanged(nameof(ClosedFeatureCount));
            OnPropertyChanged(nameof(PartialFeatureCount));
            OnPropertyChanged(nameof(HasFeatureSelections));
            OnPropertyChanged(nameof(HoleSummary));
            PointCloudVertices = verts;
            ApplySelection();
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Apply);
        }
        else
        {
            Apply();
        }
    }

    private void ApplySelection()
    {
        var boundary = SelectedFeature?.Feature.Boundary ?? _legacyPoints;
        PathPoints.Clear();
        for (int i = 0; i < boundary.Count; i++)
        {
            var point = boundary[i];
            PathPoints.Add(new FeaturePathPoint
            {
                NodeId = $"P{i + 1:D2}", X = point.X, Y = point.Y, Z = point.Z, IsActive = i == 0,
            });
        }
        SelectedPathPoint = PathPoints.FirstOrDefault();
        ScannerBoundaryPoints = SelectedFeature?.Feature.ScannerBoundary
            .Select(p => new Point3D(p.X, p.Y, p.Z)).ToArray() ?? [];
        OnPropertyChanged(nameof(SelectedFeatureStatus));
        OnPropertyChanged(nameof(SelectedFeatureIsClosed));
    }
}
