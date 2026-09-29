using System.Windows;
using System.Windows.Controls;
using ThreeDScan.Models;

namespace ThreeDScan.Controls;

public partial class HeadFeaturePanel : UserControl
{
    public HeadFeaturePanel()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty FeaturesProperty = DependencyProperty.Register(
        nameof(Features), typeof(HeadFeatureDisplayInfo), typeof(HeadFeaturePanel));

    public HeadFeatureDisplayInfo? Features
    {
        get
        {
            return (HeadFeatureDisplayInfo?)GetValue(FeaturesProperty);
        }
        set
        {
            SetValue(FeaturesProperty, value);
        }
    }
}
