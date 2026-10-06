using System.Windows.Controls;
using ThreeDScan.Domain.Models;
using ThreeDScan.ViewModels;

namespace ThreeDScan.Views;

public partial class FeedingView : UserControl
{
    public FeedingView()
    {
        InitializeComponent();
    }

    private void OnRegionChosen(object sender, LineScanRegion? region)
    {
        if (DataContext is FeedingViewModel vm)
        {
            vm.ApplyChosenRegion(region);
        }
    }
}
