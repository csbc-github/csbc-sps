using System.Windows;
using System.Windows.Controls;
using ThreeDScan.Models;

namespace ThreeDScan.Controls;

public partial class CoordinatePanel : UserControl
{
    public CoordinatePanel() { InitializeComponent(); }

    public static readonly DependencyProperty MachineCoordProperty = DependencyProperty.Register(
        nameof(MachineCoord), typeof(CoordinateInfo), typeof(CoordinatePanel));

    public CoordinateInfo? MachineCoord
    {
        get => (CoordinateInfo?)GetValue(MachineCoordProperty);
        set => SetValue(MachineCoordProperty, value);
    }

    public static readonly DependencyProperty WorkpieceCoordProperty = DependencyProperty.Register(
        nameof(WorkpieceCoord), typeof(CoordinateInfo), typeof(CoordinatePanel));

    public CoordinateInfo? WorkpieceCoord
    {
        get => (CoordinateInfo?)GetValue(WorkpieceCoordProperty);
        set => SetValue(WorkpieceCoordProperty, value);
    }
}
