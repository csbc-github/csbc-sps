using ThreeDScan.Common;
using ThreeDScan.Models;

namespace ThreeDScan.ViewModels;

// 割切區 VM：對應 SA UC-04 面掃描（割切區）／ SD §10.4
public class CuttingViewModel : ViewModelBase
{
    private bool _is3DMode = true;
    private CoordinateInfo _machineCoordinate = new(512.880, 256.420, 45.330);
    private CoordinateInfo _workpieceCoordinate = new(388.650, 198.120, 41.280);

    public bool Is2DMode { get
    {
        return !_is3DMode;
    } set { if (SetProperty(ref _is3DMode, !value)) OnPropertyChanged(nameof(Is3DMode)); } }
    public bool Is3DMode { get
    {
        return _is3DMode;
    } set { if (SetProperty(ref _is3DMode, value)) OnPropertyChanged(nameof(Is2DMode)); } }

    public CoordinateInfo MachineCoordinate { get
    {
        return _machineCoordinate;
    } set
    {
        SetProperty(ref _machineCoordinate, value);
    } }
    public CoordinateInfo WorkpieceCoordinate { get
    {
        return _workpieceCoordinate;
    } set
    {
        SetProperty(ref _workpieceCoordinate, value);
    } }
}
