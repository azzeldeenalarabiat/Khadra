namespace Khadra.Bff.Security;

internal static class BffConstants
{
    public const string CookieScheme = "KhadraSession";
    // Cookie names are per deployment now: BffSecuritySettings.SessionCookieName and friends.
    public const string XsrfHeaderName = "X-XSRF-TOKEN";

    /// <summary>The cluster that renders the customer website's pages (Frontend = Proxy).</summary>
    public const string FrontendClusterId = "web";

    /// <summary>Carries BffSecurity:FrontendSharedSecret to the renderer.</summary>
    public const string EdgeSecretHeaderName = "X-Khadra-Edge";

    public const string AccessTokenName = "access_token";
    public const string RefreshTokenName = "refresh_token";
    public const string AccessExpiresAtName = "expires_at";
    public const string RefreshExpiresAtName = "refresh_expires_at";

    public const string EmailClaim = "khadra:email";
    public const string PhoneClaim = "khadra:phone";
    public const string EmailVerifiedClaim = "khadra:email_verified";
    public const string MustChangePasswordClaim = "khadra:must_change_password";
}
