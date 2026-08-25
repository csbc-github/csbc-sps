using System.Collections.ObjectModel;
using ThreeDScan.Common;
using ThreeDScan.Models;

namespace ThreeDScan.ViewModels;

// 研磨區 VM：對應 SA UC-05 面掃描特徵（特徵座標序列）／ SD §10.5
public class GrindingViewModel : ViewModelBase
{
    private bool _is3DMode = true;
    private FeaturePathPoint? _selectedPathPoint;

    public ObservableCollection<FeaturePathPoint> PathPoints { get; } = new();

    public bool Is2DMode { get
    {
        return !_is3DMode;
    } set { if (SetProperty(ref _is3DMode, !value)) OnPropertyChanged(nameof(Is3DMode)); } }
    public bool Is3DMode { get
    {
        return _is3DMode;
    } set { if (SetProperty(ref _is3DMode, value)) OnPropertyChanged(nameof(Is2DMode)); } }

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

    public GrindingViewModel()
    {
        LoadDesignTimeData();
    }

    private void LoadDesignTimeData()
    {
        // 12 個節點假資料：待接入 UC-05 之 /api/scan/surface/feature 回應
        double[][] rows =
        {
            new[] { 100.00, 50.00, 30.00 },
            new[] { 150.50, 52.30, 31.20 },
            new[] { 200.80, 55.10, 32.50 },
            new[] { 250.20, 58.80, 33.80 },
            new[] { 300.50, 62.40, 35.20 },
            new[] { 350.10, 65.90, 36.50 },
            new[] { 400.80, 68.20, 37.80 },
            new[] { 450.30, 70.60, 39.10 },
            new[] { 500.00, 72.80, 40.40 },
            new[] { 550.40, 74.90, 41.60 },
            new[] { 600.70, 76.80, 42.80 },
            new[] { 650.20, 78.50, 44.00 },
        };
        for (int i = 0; i < rows.Length; i++)
        {
            PathPoints.Add(new FeaturePathPoint
            {
                NodeId = $"P{i + 1:D2}",
                X = rows[i][0],
                Y = rows[i][1],
                Z = rows[i][2],
                IsActive = i == 4,
            });
        }
        SelectedPathPoint = PathPoints.Count > 4 ? PathPoints[4] : PathPoints.FirstOrDefault();
    }
}
