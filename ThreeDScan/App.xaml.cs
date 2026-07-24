using System.Windows;
using Microsoft.Extensions.Configuration;
using ThreeDScan.Auth;
using ThreeDScan.Views;

namespace ThreeDScan;

public partial class App : Application
{
    /// <summary>
    /// 啟動入口：若 <c>Authentication:Enabled=true</c>，先取得 IdP Token 再開主視窗。
    /// <list type="bullet">
    /// <item><c>Flow=ClientCredentials</c>：無人全自動換 token（失敗則結束）。</item>
    /// <item><c>Flow=Interactive</c>：顯示登入視窗（授權碼）。</item>
    /// </list>
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = AppConfiguration.Load();
        var auth = config.GetSection(AuthenticationOptions.SectionName).Get<AuthenticationOptions>()
                   ?? new AuthenticationOptions();

        if (auth.Enabled)
        {
            if (auth.IsClientCredentialsFlow)
            {
                if (!TryClientCredentialsLogin(auth))
                {
                    Shutdown();
                    return;
                }
            }
            else
            {
                var login = new LoginWindow(config);
                var ok = login.ShowDialog() == true && !string.IsNullOrEmpty(AuthSession.Current.AccessToken);
                if (!ok)
                {
                    Shutdown();
                    return;
                }
            }
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    /// <summary>Client Credentials 全自動換票；失敗時提示並回 <c>false</c>。</summary>
    private static bool TryClientCredentialsLogin(AuthenticationOptions auth)
    {
        try
        {
            using var service = new ClientCredentialsLoginService(auth);
            // 診斷中：LoginAsync 可能只回傳 principal、尚未寫入 AuthSession
            var ok = service.LoginAsync().GetAwaiter().GetResult();
            if (ok)
                return true;

            MessageBox.Show(
                "Client Credentials 自動取得 Token 失敗。\n" +
                $"請查看 Log：\n{AuthDebugLog.FilePath}",
                "身分驗證",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Client Credentials 自動登入錯誤：\n{ex.Message}\n\n" +
                $"Log：{AuthDebugLog.FilePath}",
                "身分驗證",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }
}
