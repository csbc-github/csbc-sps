using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using ThreeDScan.Application.Scan;
using ThreeDScan.Common;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Scan;
using ThreeDScan.Models;

namespace ThreeDScan.ViewModels;

/// <summary>研磨區：顯示本次孔洞 featureBoundary。</summary>
public class GrindingViewModel : ViewModelBase
{
    private bool _is3DMode = true;
    private FeaturePathPoint? _selectedPathPoint;
    private FileSystemWatcher? _watch;
    private int _watchVersion;
    private readonly string _contentsRoot;
    private readonly IHoleBoundaryReader _boundary;

    /// <summary>邊界座標序列。</summary>
    public ObservableCollection<FeaturePathPoint> PathPoints { get; } = new();

    /// <summary>2D 模式。</summary>
    public bool Is2DMode
    {
        get
        {
            return !_is3DMode;
        }
        set
        {
            if (SetProperty(ref _is3DMode, !value))
            {
                OnPropertyChanged(nameof(Is3DMode));
            }
        }
    }

    /// <summary>3D 模式。</summary>
    public bool Is3DMode
    {
        get
        {
            return _is3DMode;
        }
        set
        {
            if (SetProperty(ref _is3DMode, value))
            {
                OnPropertyChanged(nameof(Is2DMode));
            }
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

    /// <summary>延遲後把監看檔結果綁到清單。</summary>
    /// <param name="version">這次重載序號；過期則略過。</param>
    private async Task ReloadAfterDelayAsync(int version)
    {
        await Task.Delay(200).ConfigureAwait(true);
        if (version != _watchVersion)
        {
            return;
        }

        ApplyBoundary();
    }

    /// <summary>讀邊界並更新 <see cref="PathPoints"/>。</summary>
    private void ApplyBoundary()
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

        void Apply()
        {
            PathPoints.Clear();
            foreach (var point in points)
            {
                PathPoints.Add(point);
            }

            SelectedPathPoint = PathPoints.FirstOrDefault();
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
