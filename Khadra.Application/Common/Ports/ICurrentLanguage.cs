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
    Language Current { get; }
}
