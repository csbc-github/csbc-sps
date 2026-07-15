using Microsoft.Extensions.Configuration;

namespace ThreeDScan;

/// <summary>載入 appsettings；Debug 建置時疊加 appsettings.Development.json。</summary>
internal static class AppConfiguration
{
    public static IConfigurationRoot Load()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

#if DEBUG
        builder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
#endif

        return builder.Build();
    }
}
