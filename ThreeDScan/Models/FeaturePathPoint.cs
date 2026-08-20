using ThreeDScan.Common;

namespace ThreeDScan.Models;

// 研磨區特徵座標序列節點（對應 SA UC-05 面掃描特徵 / SD §10.5）
public class FeaturePathPoint : ViewModelBase
{
    private string _nodeId = string.Empty;
    private double _x;
    private double _y;
    private double _z;
    private bool _isActive;

    public string NodeId { get
    {
        return _nodeId;
    } set
    {
        SetProperty(ref _nodeId, value);
    } }  // 例如 P01~P12
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
    public bool IsActive { get
    {
        return _isActive;
    } set
    {
        SetProperty(ref _isActive, value);
    } }
}
