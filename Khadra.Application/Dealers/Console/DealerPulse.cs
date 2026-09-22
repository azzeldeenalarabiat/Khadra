using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Khadra.Application.Bookings.ReadModels;

namespace Khadra.Application.Dealers.Console;

/// <summary>
/// Turns a <see cref="DealerQueueSignature"/> into the one opaque string the console compares.
/// </summary>
/// <remarks>
/// A HASH rather than the numbers themselves, for one reason: the console must not be able to read a
/// figure out of it. The moment a pulse carries something renderable, somebody renders it, and then
/// the queue's count and the badge's count come from two endpoints that can disagree.
///
/// SHA-256 rather than <c>GetHashCode</c>, which is randomised per process on strings — behind two
/// API instances the token would flip on every other poll and the console would re-read for ever.
/// Not a security property: the input is counts a dealer already knows.
/// </remarks>
public static class DealerPulse
{
    /// <summary>
    /// Sixteen hex characters. Long enough that two different queues colliding is not a thing that
    /// happens, short enough to be unremarkable in a log.
    /// </summary>
    private const int TokenLength = 16;

    public static string TokenFor(DealerQueueSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var builder = new StringBuilder();

        // Ordered, because a grouped query promises no order and an unordered join would hand back a
        // different token for an unchanged queue -- which reads as "something moved" and re-reads
        // everything, every poll, for ever.
        foreach (var entry in signature.ByStatus.OrderBy(entry => entry.Status, StringComparer.Ordinal))
        {
            builder.Append(entry.Status)
                .Append(':')
                .Append(entry.Count.ToString(CultureInfo.InvariantCulture))
                .Append('|');
        }

        builder.Append("live:").Append(signature.Live.ToString(CultureInfo.InvariantCulture))
            .Append("|disputed:").Append(signature.Disputed.ToString(CultureInfo.InvariantCulture));

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(digest)[..TokenLength].ToLowerInvariant();
    }
}
