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
    private CoordinateInfo _tailLeft = new();
    private CoordinateInfo _tailRight = new();
    private CoordinateInfo _tailTop = new();
    private double _width;
    private double _height;
    private double _length;
    private double _leftWidth;
    private double _rightWidth;
    private double _leftLength;
    private double _rightLength;

    public string Name { get
    {
        return _name;
    } set
    {
        SetProperty(ref _name, value);
    } }
    public ScanStatus Status { get
    {
        return _status;
    } set
    {
        SetProperty(ref _status, value);
    } }

    // 左邊角點（依 SA §7 角鋼三特徵點定義）
    public CoordinateInfo LeftCorner { get
    {
        return _leftCorner;
    } set
    {
        SetProperty(ref _leftCorner, value);
    } }
    // 右邊角點
    public CoordinateInfo RightCorner { get
    {
        return _rightCorner;
    } set
    {
        SetProperty(ref _rightCorner, value);
    } }
    // 頂點（倒 V 交會處）
    public CoordinateInfo TopPoint { get
    {
        return _topPoint;
    } set
    {
        SetProperty(ref _topPoint, value);
    } }

    /// <summary>尾端左邊角點 (mm)。</summary>
    public CoordinateInfo TailLeft { get
    {
        return _tailLeft;
    } set
    {
        SetProperty(ref _tailLeft, value);
    } }

    /// <summary>尾端右邊角點 (mm)。</summary>
    public CoordinateInfo TailRight { get
    {
        return _tailRight;
    } set
    {
        SetProperty(ref _tailRight, value);
    } }

    /// <summary>尾端頂點 (mm)。</summary>
    public CoordinateInfo TailTop { get
    {
        return _tailTop;
    } set
    {
        SetProperty(ref _tailTop, value);
    } }

    /// <summary>整支角鋼沿滾輪的長度 (mm)。</summary>
    public double Length { get
    {
        return _length;
    } set
    {
        SetProperty(ref _length, value);
    } }

    /// <summary>左邊翼寬 (mm)。</summary>
    public double LeftWidth { get
    {
        return _leftWidth;
    } set
    {
        SetProperty(ref _leftWidth, value);
    } }

    /// <summary>右邊翼寬 (mm)。</summary>
    public double RightWidth { get
    {
        return _rightWidth;
    } set
    {
        SetProperty(ref _rightWidth, value);
    } }

    /// <summary>該件全部點的 X 跨距 (mm)。</summary>
    public double Width { get
    {
        return _width;
    } set
    {
        SetProperty(ref _width, value);
    } }

    /// <summary>高度 (mm)。</summary>
    public double Height { get
    {
        return _height;
    } set
    {
        SetProperty(ref _height, value);
    } }

    /// <summary>左邊長度 (mm)。</summary>
    public double LeftLength { get
    {
        return _leftLength;
    } set
    {
        SetProperty(ref _leftLength, value);
    } }

    /// <summary>右邊長度 (mm)。</summary>
    public double RightLength { get
    {
        return _rightLength;
    } set
    {
        SetProperty(ref _rightLength, value);
    } }
}
