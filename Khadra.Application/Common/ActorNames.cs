using System.Globalization;
using Khadra.Domain.Common;

namespace Khadra.Application.Common;

/// <summary>
/// The name an authenticated actor is RECORDED under, in a row that is never rewritten (pre-launch item 103).
/// </summary>
/// <remarks>
/// Every token this platform issues carries the person's name (<c>JwtAccessTokenIssuer</c>, from the user's required
/// name), so the claim is always there. The writers used to fall back to the English words "Unknown admin" and
/// "Unknown" for a request without it, which an Arabic screen would have shown in English for ever. They fall back
/// instead to the actor's short reference — the first eight hex digits of their id, the way the consoles shorten every
/// id — which reads the same in both languages; the id itself is stored beside the name on every one of those records.
/// </remarks>
public static class ActorNames
{
    public static string RecordedName(this ICurrentActor actor, Id actorUserId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return string.IsNullOrWhiteSpace(actor.Name) ? Reference(actorUserId) : actor.Name;
    }

    public static string Reference(Id id) => id.Value.ToString("N", CultureInfo.InvariantCulture)[..8];
}
