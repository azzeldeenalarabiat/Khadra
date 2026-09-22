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
}
