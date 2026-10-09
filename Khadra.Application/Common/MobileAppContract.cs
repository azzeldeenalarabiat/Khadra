namespace Khadra.Application.Common;

/// <summary>
/// The customer app's half of the version gate, named once.
/// </summary>
/// <remarks>
/// Here rather than beside the middleware, because both sides are held to these names: the API emits
/// them, the app sends and matches them, and the app's own test of the codes it translates reads the
/// server's Application sources to prove each one is really emitted.
/// </remarks>
public static class MobileAppContract
{
    /// <summary>
    /// Sent by every build from 1.1.0 on every request to this API, with the version it was built as
    /// (<c>1.1.0+2</c>; the part after <c>+</c> is ignored).
    /// </summary>
    public const string VersionHeader = "X-Khadra-App-Version";

    /// <summary>The code of the 426 refusal. Clients key on this, never on the status alone.</summary>
    public const string UpdateRequiredCode = "app.update_required";

    // ── Rules a build learned, by the release that learned them (Wave 7) ──────────────────────────────────────────
    //
    // TEMPORARY, and not a security boundary. Each names the first build that can answer a rule the server holds every
    // other caller to. An app build that DECLARES an older version is spared that one rule (ClientInfo.PredatesRule),
    // only until MobileApp:MinimumSupportedVersion reaches it and refuses the build outright, which closes the bridge
    // and the hole a declared version opens with it. Build facts, the same in every environment, so constants and never
    // configuration. Once the minimum is raised to 1.4.0 and verified, the close-out deletes all three and every branch
    // that reads them (pre-launch item 239). From the release that raises the app to 1.4.0, MobileAppMinimumVersionTests
    // pins each at or below the app's own version, so the app in this repository is never spared a rule it was built to
    // answer.

    /// <summary>Consent is asked at registration and judged on every request (pre-launch items 224 and 238).</summary>
    public static readonly AppVersion ConsentAwareFrom = AppVersion.Parse("1.4.0");

    /// <summary>A handover code is issued only once its handover's window has opened (pre-launch item 225).</summary>
    public static readonly AppVersion HandoverWindowAwareFrom = AppVersion.Parse("1.4.0");

    /// <summary>A customer's copy of a decided dispute carries only their own share (owner decision 3; item 151).</summary>
    public static readonly AppVersion DisputeSharesWithheldFrom = AppVersion.Parse("1.4.0");
}
