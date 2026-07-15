using ThreeDScan.Common;

namespace ThreeDScan.Models;

// 單一角鋼掃描結果（對應 SA UC-03 線掃描：左邊角點、右邊角點、頂點）
public class MaterialItem : ViewModelBase
{
    private string _name = string.Empty;
    private ScanStatus _status;
    private CoordinateInfo _leftCorner = new();
    private CoordinateInfo _rightCorner = new();
    private CoordinateInfo _topPoint = new();

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public ScanStatus Status { get => _status; set => SetProperty(ref _status, value); }

    // 左邊角點（依 SA §7 角鋼三特徵點定義）
    public CoordinateInfo LeftCorner { get => _leftCorner; set => SetProperty(ref _leftCorner, value); }
    // 右邊角點
    public CoordinateInfo RightCorner { get => _rightCorner; set => SetProperty(ref _rightCorner, value); }
    // 頂點（倒 V 交會處）
    public CoordinateInfo TopPoint { get => _topPoint; set => SetProperty(ref _topPoint, value); }
}
