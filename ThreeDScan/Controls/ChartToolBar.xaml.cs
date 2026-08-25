using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ThreeDScan.Controls;

public partial class ChartToolBar : UserControl
{
    public ChartToolBar() { InitializeComponent(); }

    public static readonly DependencyProperty HomeCommandProperty =
        DependencyProperty.Register(nameof(HomeCommand), typeof(ICommand), typeof(ChartToolBar));
    public ICommand? HomeCommand { get
    {
        return (ICommand?)GetValue(HomeCommandProperty);
    } set
    {
        SetValue(HomeCommandProperty, value);
    } }

    public static readonly DependencyProperty BackCommandProperty =
        DependencyProperty.Register(nameof(BackCommand), typeof(ICommand), typeof(ChartToolBar));
    public ICommand? BackCommand { get
    {
        return (ICommand?)GetValue(BackCommandProperty);
    } set
    {
        SetValue(BackCommandProperty, value);
    } }

    public static readonly DependencyProperty ForwardCommandProperty =
        DependencyProperty.Register(nameof(ForwardCommand), typeof(ICommand), typeof(ChartToolBar));
    public ICommand? ForwardCommand { get
    {
        return (ICommand?)GetValue(ForwardCommandProperty);
    } set
    {
        SetValue(ForwardCommandProperty, value);
    } }

    public static readonly DependencyProperty PanCommandProperty =
        DependencyProperty.Register(nameof(PanCommand), typeof(ICommand), typeof(ChartToolBar));
    public ICommand? PanCommand { get
    {
        return (ICommand?)GetValue(PanCommandProperty);
    } set
    {
        SetValue(PanCommandProperty, value);
    } }

    public static readonly DependencyProperty ZoomCommandProperty =
        DependencyProperty.Register(nameof(ZoomCommand), typeof(ICommand), typeof(ChartToolBar));
    public ICommand? ZoomCommand { get
    {
        return (ICommand?)GetValue(ZoomCommandProperty);
    } set
    {
        SetValue(ZoomCommandProperty, value);
    } }

    public static readonly DependencyProperty SettingsCommandProperty =
        DependencyProperty.Register(nameof(SettingsCommand), typeof(ICommand), typeof(ChartToolBar));
    public ICommand? SettingsCommand { get
    {
        return (ICommand?)GetValue(SettingsCommandProperty);
    } set
    {
        SetValue(SettingsCommandProperty, value);
    } }

    public static readonly DependencyProperty CompareCommandProperty =
        DependencyProperty.Register(nameof(CompareCommand), typeof(ICommand), typeof(ChartToolBar));
    public ICommand? CompareCommand { get
    {
        return (ICommand?)GetValue(CompareCommandProperty);
    } set
    {
        SetValue(CompareCommandProperty, value);
    } }

    public static readonly DependencyProperty SaveCommandProperty =
        DependencyProperty.Register(nameof(SaveCommand), typeof(ICommand), typeof(ChartToolBar));
    public ICommand? SaveCommand { get
    {
        return (ICommand?)GetValue(SaveCommandProperty);
    } set
    {
        SetValue(SaveCommandProperty, value);
    } }
}
