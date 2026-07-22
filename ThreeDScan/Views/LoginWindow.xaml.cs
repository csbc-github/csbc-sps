using System.Windows;
using Microsoft.Extensions.Configuration;
using ThreeDScan.Auth;

namespace ThreeDScan.Views;

/// <summary>
/// 身分驗證視窗：觸發甲方 IdP Authorization Code 登入。
/// 成功時 <see cref="Window.DialogResult"/> 為 <c>true</c>，Token 已寫入 <see cref="AuthSession.Current"/>。
/// </summary>
public partial class LoginWindow : Window
{
    private readonly AuthenticationOptions _options;
    private readonly OidcDesktopLoginService _login;

    /// <summary>自設定綁定 Authentication 選項並建立登入服務。</summary>
    public LoginWindow(IConfiguration config)
    {
        InitializeComponent();
        _options = config.GetSection(AuthenticationOptions.SectionName).Get<AuthenticationOptions>()
                   ?? new AuthenticationOptions();
        _login = new OidcDesktopLoginService(_options);
    }

    /// <summary>「以 IDP 登入」：執行 OIDC 流程；成功則關閉視窗並回傳 DialogResult=true。</summary>
    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        BusyBar.Visibility = Visibility.Visible;
        StatusText.Text = "正在開啟甲方登入頁面…";

        try
        {
            var ok = await _login.LoginAsync();
            if (ok)
            {
                StatusText.Text = $"登入成功：{AuthSession.Current.DisplayName ?? "（已取得 Token）"}";
                DialogResult = true;
                Close();
                return;
            }

            StatusText.Text = "登入失敗或已取消，請重試。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"登入錯誤：{ex.Message}";
            MessageBox.Show(ex.Message, "身分驗證", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BusyBar.Visibility = Visibility.Collapsed;
            LoginButton.IsEnabled = true;
        }
    }

    /// <summary>取消登入：DialogResult=false，由 App 決定是否結束程序。</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
