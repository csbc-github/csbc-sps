using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using ThreeDScan.Application.Scan;
using ThreeDScan.Common;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Scan;
using ThreeDScan.Models;
using ThreeDScan.Services;

namespace ThreeDScan.ViewModels;

/// <summary>研磨區：監看孔洞邊界檔，左側僅顯示 3D 點雲、右側顯示座標表。</summary>
/// <remarks>
/// 點雲來自監看檔 <c>rawPlyPath</c>（掃描器座標）；
/// <see cref="PathPoints"/> 來自 <c>featureBoundary</c>（公開絕對座標）。兩者座標系不同，3D 視窗不疊孔緣。
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

        var points = new List<FeaturePathPoint>();
        var i = 1;
        foreach (var p in snapshot.Points)
        {
            points.Add(new FeaturePathPoint
            {
                NodeId = $"P{i:D2}",
                X = p.X,
                Y = p.Y,
                Z = p.Z,
                IsActive = i == 1,
            });
            i++;
        }

        // 左側 3D：僅載入 raw PLY；舊版監看檔無 rawPlyPath 時維持佔位。
        IReadOnlyList<PointCloudRenderVertex> verts = Array.Empty<PointCloudRenderVertex>();
        if (!string.IsNullOrWhiteSpace(snapshot.RawPlyPath) && File.Exists(snapshot.RawPlyPath))
        {
            try
            {
                verts = await AsciiPlyPointCloudDisplayLoader.LoadAsync(snapshot.RawPlyPath, CancellationToken.None)
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
            PathPoints.Clear();
            foreach (var point in points)
            {
                PathPoints.Add(point);
            }

            SelectedPathPoint = PathPoints.FirstOrDefault();
            PointCloudVertices = verts;
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
}