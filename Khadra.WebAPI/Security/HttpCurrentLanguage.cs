using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Microsoft.Net.Http.Headers;

namespace Khadra.WebAPI.Security;

/// <summary>
/// The caller's language, from `Accept-Language`.
/// </summary>
/// <remarks>
/// <para>
/// A HEADER rather than a query parameter, for one practical reason: a parameter has to be threaded
/// through every call site, and the one that gets forgotten shows the default language to somebody
/// who asked for the other — silently, and only on that screen. A header is set once, by the client,
/// for every request it makes.
/// </para>
/// <para>
/// **It never fails.** Anything unrecognised — a language the platform does not have, a malformed
/// value, nothing at all — is <see cref="Language.Default"/>. A header no customer can see must not
/// be able to refuse their request, and there is no negotiation worth doing between two languages.
/// </para>
/// <para>
/// Quality values ARE honoured, through the framework's own parser: a browser sending
/// `en-US,en;q=0.9,ar;q=0.8` means English, and reading only the first tag would get that right by
/// luck and `ar;q=0.3,en;q=0.9` wrong.
/// </para>
/// </remarks>
internal sealed class HttpCurrentLanguage(IHttpContextAccessor accessor) : ICurrentLanguage
{
    public Language Current
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is null)
                return Language.Default;

            var accepted = context.Request.GetTypedHeaders().AcceptLanguage;
            if (accepted is null || accepted.Count == 0)
                return Language.Default;

            foreach (var entry in accepted.OrderByDescending(header => header.Quality ?? 1d))
            {
                // `ar-JO` is Arabic. The region never changes which of the two this is.
                var tag = entry.Value.Value;
                if (string.IsNullOrWhiteSpace(tag))
                    continue;

                var primary = tag.Split('-')[0];
                var match = Enumeration.GetAll<Language>()
                    .FirstOrDefault(language =>
                        string.Equals(primary, language.Name, StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                    return match;
            }

            return Language.Default;
        }
    }
}
