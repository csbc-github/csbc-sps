using System.Windows;

namespace ThreeDScan.Common;

/// <summary>WPF 訊息框輔助（確保在 UI 執行緒顯示）。</summary>
internal static class WpfMessageBox
{
    /// <summary>顯示資訊訊息。</summary>
    public static void ShowInfo(string message, string title)
    {
        Show(message, title, MessageBoxImage.Information);
    }

    /// <summary>顯示警告訊息。</summary>
    public static void ShowWarning(string message, string title)
    {
        Show(message, title, MessageBoxImage.Warning);
    }

    /// <summary>顯示錯誤訊息（圖示為 Warning，與既有對話框風格一致）。</summary>
    public static void ShowError(string message, string title)
    {
        Show(message, title, MessageBoxImage.Warning);
    }

    /// <summary>
    /// 顯示訊息框；若目前不在 UI 執行緒則透過 <see cref="Application.Dispatcher"/> 切換。
    /// </summary>
    public static void Show(string message, string title, MessageBoxImage icon)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var app = Application.Current;
        if (app?.Dispatcher is null)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, icon);
            return;
        }

        if (app.Dispatcher.CheckAccess())
            MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        else
            app.Dispatcher.Invoke(() => MessageBox.Show(message, title, MessageBoxButton.OK, icon));
    }
}
