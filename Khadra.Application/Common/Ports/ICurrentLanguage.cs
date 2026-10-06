using Khadra.Domain.Common;

namespace Khadra.Application.Common.Ports;

/// <summary>
/// The language the caller of this request reads in.
/// </summary>
/// <remarks>
/// <para>
/// A transport fact, like <c>ICurrentActor</c> and <c>ClientInfo</c>, so it is a port rather than
/// something a handler works out. Controllers read it and put a <see cref="Language"/> on the query;
/// handlers and readers receive the value, never the port. That is what lets a test say
/// <c>Language.Arabic</c> without an HTTP context existing.
/// </para>
/// <para>
/// It is per REQUEST, and deliberately not the same thing as an account's preferred language for
/// email. A customer may read the app in Arabic on their phone and their email in English; deriving
/// either from the other would make one of them wrong.
/// </para>
/// </remarks>
public interface ICurrentLanguage
{
    /// <summary>The language to answer in: <see cref="Stated"/>, or <see cref="Language.Default"/>.</summary>
    Language Current { get; }

    /// <summary>
    /// The language this request named, or null when it named none the platform has. Where a fallback other than the
    /// default is right — a customer's stored language for the checkout's return address (Fix & Polish Wave 3, E3) —
    /// this is the one to read, because <see cref="Current"/> cannot tell "English" from "nothing said".
    /// </summary>
    Language? Stated { get; }
}
