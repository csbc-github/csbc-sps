using ThreeDScan.Common;

namespace ThreeDScan.Models;

// 通用 3D 座標（公釐）— SA §1.4／SD §5.1 均以 mm 為單位
public class CoordinateInfo : ViewModelBase
{
    private double _x;
    private double _y;
    private double _z;

    public double X { get
    {
        return _x;
    } set
    {
        SetProperty(ref _x, value);
    } }
    public double Y { get
    {
        return _y;
    } set
    {
        SetProperty(ref _y, value);
    } }
    public double Z { get
    {
        return _z;
    } set
    {
        SetProperty(ref _z, value);
    } }

    public CoordinateInfo() { }

    public CoordinateInfo(double x, double y, double z)
    {
        _x = x; _y = y; _z = z;
    }

    public override string ToString()
    {
        return $"({X:F3}, {Y:F3}, {Z:F3}) mm";
    }
}
