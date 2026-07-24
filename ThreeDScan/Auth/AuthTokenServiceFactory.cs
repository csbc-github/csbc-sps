namespace ThreeDScan.Auth;

/// <summary>依 <see cref="AuthenticationOptions.Flow"/> 建立對應的 token 來源。</summary>
public static class AuthTokenServiceFactory
{
    public static IAccessTokenSource Create(AuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.IsClientCredentialsFlow
            ? new ClientCredentialsLoginService(options)
            : new OidcDesktopLoginService(options);
    }
}
