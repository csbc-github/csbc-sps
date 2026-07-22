using System.Windows;
using Microsoft.Extensions.Configuration;
using ThreeDScan.Auth;
using ThreeDScan.Views;

namespace ThreeDScan;

public partial class App : Application
{
    /// <summary>
    /// 啟動入口：若 <c>Authentication:Enabled=true</c>，先完成 IdP 登入再開主視窗；
    /// 登入失敗或取消則結束程序。WPF 設定來自 <see cref="AppConfiguration.Load"/>（Debug 會疊加 Development）。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = AppConfiguration.Load();
        var auth = config.GetSection(AuthenticationOptions.SectionName).Get<AuthenticationOptions>()
                   ?? new AuthenticationOptions();

        // IdP 閘道：未啟用則直接進主畫面（本機開發）
        if (auth.Enabled)
        {
            var login = new LoginWindow(config);
            var ok = login.ShowDialog() == true && !string.IsNullOrEmpty(AuthSession.Current.AccessToken);
            if (!ok)
            {
                Shutdown();
                return;
            }
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }
}
