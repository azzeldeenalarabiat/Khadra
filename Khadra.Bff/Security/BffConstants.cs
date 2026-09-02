namespace Khadra.Bff.Security;

internal static class BffConstants
{
    public const string CookieScheme = "KhadraSession";
    public const string SessionCookieName = "__Host-Khadra.Session";
    public const string AntiforgeryCookieName = "__Host-Khadra.Antiforgery";
    public const string XsrfCookieName = "XSRF-TOKEN";
    public const string XsrfHeaderName = "X-XSRF-TOKEN";

    public const string AccessTokenName = "access_token";
    public const string RefreshTokenName = "refresh_token";
    public const string AccessExpiresAtName = "expires_at";
    public const string RefreshExpiresAtName = "refresh_expires_at";

    public const string EmailClaim = "khadra:email";
    public const string PhoneClaim = "khadra:phone";
    public const string EmailVerifiedClaim = "khadra:email_verified";
    public const string MustChangePasswordClaim = "khadra:must_change_password";
}
