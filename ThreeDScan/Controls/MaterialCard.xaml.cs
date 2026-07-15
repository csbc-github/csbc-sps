using System.Windows;
using System.Windows.Controls;
using ThreeDScan.Models;

namespace ThreeDScan.Controls;

public partial class MaterialCard : UserControl
{
    public MaterialCard() { InitializeComponent(); }

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(MaterialItem), typeof(MaterialCard));

    public MaterialItem? Item
    {
        get => (MaterialItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }
}
