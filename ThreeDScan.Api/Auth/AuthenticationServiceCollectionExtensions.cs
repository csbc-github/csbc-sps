using DAS.Authentication.Providers;
using DAS.Authentication.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ThreeDScan.Api.Hubs;

namespace ThreeDScan.Api.Auth;

/// <summary>
/// Api 身分驗證 DI／中介軟體／端點對應擴充方法。
/// 正式把關為 ASP.NET JwtBearer；DAS <see cref="AuthenticationManager"/> 供測試端點交叉驗證。
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// 註冊 Authentication 選項、DAS <see cref="AuthenticationManager"/>，
    /// 並在 <c>Authentication:Enabled=true</c> 時加入 JwtBearer 與 Authorization。
    /// SignalR 校正 Hub 可從 query <c>access_token</c> 取得 Bearer。
    /// </summary>
    public static IServiceCollection AddThreeDScanAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AuthenticationOptions>(configuration.GetSection(AuthenticationOptions.SectionName));
        var options = configuration.GetSection(AuthenticationOptions.SectionName).Get<AuthenticationOptions>()
                      ?? new AuthenticationOptions();

        // DAS 交叉驗證用（AuthController /validate）；OIDC provider 需 Authority + ClientId
        var manager = new AuthenticationManager();
        if (!string.IsNullOrWhiteSpace(options.Authority) && !string.IsNullOrWhiteSpace(options.ClientId))
        {
            manager.RegisterProvider(
                "oidc",
                new OpenIdConnectProvider(options.Authority.TrimEnd('/'), options.ClientId));
        }

        services.AddSingleton(manager);

        if (!options.Enabled)
            return services;

        if (string.IsNullOrWhiteSpace(options.Authority))
            throw new InvalidOperationException("Authentication:Enabled=true 時必須設定 Authentication:Authority。");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority.TrimEnd('/');
                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    // Audience 有填才驗證；空白則僅驗 issuer／簽章／lifetime
                    ValidateAudience = !string.IsNullOrWhiteSpace(options.Audience),
                    ValidAudience = string.IsNullOrWhiteSpace(options.Audience) ? null : options.Audience,
                    ValidateLifetime = true,
                    NameClaimType = "name",
                    RoleClaimType = "role",
                };

                jwt.Events = new JwtBearerEvents
                {
                    // SignalR 無法像 REST 一樣方便帶 Authorization header，改從 query 取 access_token
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) &&
                            path.StartsWithSegments(CalibrationPreviewHub.HubPath))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// 啟用驗證時掛上 <c>UseAuthentication</c>／<c>UseAuthorization</c>；
    /// <c>Enabled=false</c> 時略過（本機開發）。
    /// </summary>
    public static WebApplication UseThreeDScanAuthentication(this WebApplication app)
    {
        var options = app.Configuration.GetSection(AuthenticationOptions.SectionName).Get<AuthenticationOptions>()
                      ?? new AuthenticationOptions();
        if (!options.Enabled)
            return app;

        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    /// <summary>
    /// 對應 Controllers 與校正 Preview Hub；
    /// 啟用驗證時僅對 Controllers 套用 <c>RequireAuthorization</c>。
    /// Hub 暫不強制授權（見下方註解）。
    /// </summary>
    public static void MapThreeDScanAuthenticatedEndpoints(this WebApplication app)
    {
        var enabled = app.Configuration.GetValue($"{AuthenticationOptions.SectionName}:Enabled", false);
        var controllers = app.MapControllers();
        var hub = app.MapHub<CalibrationPreviewHub>(CalibrationPreviewHub.HubPath);
        if (!enabled) return;

        controllers.RequireAuthorization();
        // 監看 Hub 暫不強制 Bearer（預覽推播；業務 API 仍要授權）
        // hub.RequireAuthorization();
    }
}
