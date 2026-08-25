using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ThreeDScan.ViewModels;

namespace ThreeDScan.Views;

public partial class FrameInspectorWindow : Window
{
    private readonly FeedingViewModel _vm;
    private readonly DispatcherTimer _timer;
    private int _lastFrameNumber = -1;
    private List<FramePointRow> _rows = new();

    public FrameInspectorWindow(FeedingViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, __) => Refresh();
        _timer.Start();
        Closed += (_, __) => _timer.Stop();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        _lastFrameNumber = -1;
        Refresh();
    }

    private void Refresh()
    {
        if (PauseCheck.IsChecked == true) return;

        var f = _vm.CurrentFrame;
        if (f == null)
        {
            HeaderText.Text = "尚未開始掃描";
            return;
        }

        if (f.FrameNumber == _lastFrameNumber) return;
        _lastFrameNumber = f.FrameNumber;

        bool onlyValid = OnlyValidCheck.IsChecked == true;
        int n = f.Distances.Length;
        var rows = new List<FramePointRow>(n);
        int valid = 0;
        int distMin = int.MaxValue, distMax = int.MinValue;
        long sum = 0;
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            int d = f.Distances[i];
            bool isValid = d > 1 && d < 999000;
            string status;
            switch (d)
            {
                case 0:
                    status = "無回波";
                    break;
                case 1:
                    status = "雜訊";
                    break;
                default:
                    status = d >= 999000 ? "量程外/飽和" : "OK";
                    break;
            }
            if (isValid)
            {
                valid++;
                sum += d;
                if (d < distMin) distMin = d;
                if (d > distMax) distMax = d;
                var (x, y) = f.Cartesian[i];
                if (x < xMin) xMin = x; if (x > xMax) xMax = x;
                if (y < yMin) yMin = y; if (y > yMax) yMax = y;
            }
            if (onlyValid && !isValid) continue;
            var (cx, cy) = f.Cartesian[i];
            double angle = f.StartAngleDeg + i * f.AngleResolutionDeg;
            rows.Add(new FramePointRow
            {
                Index = i,
                AngleText = angle.ToString("F2"),
                DistanceMm = d,
                XText = cx.ToString("F1"),
                YText = cy.ToString("F1"),
                StatusText = status,
            });
        }

        _rows = rows;
        Grid.ItemsSource = _rows;

        HeaderText.Text = $"幀 #{f.FrameNumber}  ·  {n} 點  ·  起始角 {f.StartAngleDeg:F2}°  ·  解析 {f.AngleResolutionDeg:F3}°  ·  {f.TimestampUtc.ToLocalTime():HH:mm:ss.fff}";
        StatsText.Text = valid == 0
            ? "全部點為無效（無回波 / 飽和）"
            : $"有效 {valid}/{n}  ·  距離 {distMin}~{distMax} 平均 {(int)(sum / valid)} mm  ·  X [{xMin:F0} ~ {xMax:F0}] mm  ·  Y [{yMin:F0} ~ {yMax:F0}] mm";
    }

    private void OnCopySelection(object sender, RoutedEventArgs e)
    {
        var selected = Grid.SelectedItems;
        var src = selected != null && selected.Count > 0 ? (IEnumerable<object>)selected : _rows;
        var sb = new StringBuilder();
        sb.AppendLine("Index\tAngleDeg\tDistanceMm\tX\tY\tStatus");
        foreach (var o in src)
        {
            if (o is not FramePointRow r) continue;
            sb.Append(r.Index).Append('\t')
              .Append(r.AngleText).Append('\t')
              .Append(r.DistanceMm).Append('\t')
              .Append(r.XText).Append('\t')
              .Append(r.YText).Append('\t')
              .AppendLine(r.StatusText);
        }
        try { Clipboard.SetText(sb.ToString()); }
        catch { /* clipboard may be locked, ignore */ }
    }

    private void OnExportCsv(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0) return;
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"lmx_frame_{_lastFrameNumber}.csv",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            using var sw = new StreamWriter(dlg.FileName, false, new UTF8Encoding(true));
            sw.WriteLine("Index,AngleDeg,DistanceMm,X,Y,Status");
            foreach (var r in _rows)
            {
                sw.Write(r.Index); sw.Write(',');
                sw.Write(r.AngleText); sw.Write(',');
                sw.Write(r.DistanceMm); sw.Write(',');
                sw.Write(r.XText); sw.Write(',');
                sw.Write(r.YText); sw.Write(',');
                sw.WriteLine(r.StatusText);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "匯出失敗：" + ex.Message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class FramePointRow
    {
        public int Index { get; set; }
        public string AngleText { get; set; } = "";
        public int DistanceMm { get; set; }
        public string XText { get; set; } = "";
        public string YText { get; set; } = "";
        public string StatusText { get; set; } = "";
    }
}
