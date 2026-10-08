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

    /// <summary>
    /// Carries the nonce this BFF put in the page's <c>script-src</c> to the renderer (pre-launch item 222). Set only on
    /// requests to the renderer, and whatever a browser sent under the name is never forwarded anywhere.
    /// </summary>
    public const string CspNonceHeaderName = "X-Khadra-Csp-Nonce";

    /// <summary>
    /// The header a customer app build declares its version in (the API's <c>MobileAppContract.VersionHeader</c>; a test
    /// pins the two equal). Never forwarded: nothing behind this BFF is the customer app (Wave 4, W4-8).
    /// </summary>
    public const string CustomerAppVersionHeaderName = "X-Khadra-App-Version";

    public const string AccessTokenName = "access_token";
    public const string RefreshTokenName = "refresh_token";
    public const string AccessExpiresAtName = "expires_at";
    public const string RefreshExpiresAtName = "refresh_expires_at";

    public const string EmailClaim = "khadra:email";
    public const string PhoneClaim = "khadra:phone";
    public const string EmailVerifiedClaim = "khadra:email_verified";
    public const string MustChangePasswordClaim = "khadra:must_change_password";
}
