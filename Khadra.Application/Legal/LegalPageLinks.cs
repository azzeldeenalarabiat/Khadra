using Khadra.Application.Common.Ports;
using Khadra.Application.Legal.ReadModels;
using Khadra.Application.PlatformSettings.AppConfig;
using Khadra.Domain.Legal;

namespace Khadra.Application.Legal;

/// <summary>
/// A legal text in force as every client reads it: <c>/app-config</c>'s <c>legal</c> block, and the texts a person has
/// still to accept (Wave 4, W4-8). One shape and one set of links, so a screen treats both alike.
/// </summary>
public static class LegalPageLinks
{
    /// <summary>The website's page for a document, in each language: the same paths the website serves.</summary>
    /// <returns>Null while <c>App:CustomerAppBaseUrl</c> is not set: no link is invented then.</returns>
    public static LegalPageUrlsDto? For(ICustomerSiteSettings site, string slug)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (site.BaseUrl is not { } baseUrl)
            return null;
        var root = baseUrl.AbsoluteUri.TrimEnd('/');
        return new LegalPageUrlsDto($"{root}/en/{slug}", $"{root}/ar/{slug}");
    }

    public static LegalConfigDocumentDto Describe(
        ICustomerSiteSettings site,
        LegalDocumentKind kind,
        Guid versionId,
        string versionLabel,
        DateTimeOffset effectiveFrom)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return new LegalConfigDocumentDto(kind.Name, kind.Slug, versionId, versionLabel, effectiveFrom, For(site, kind.Slug));
    }

    public static LegalConfigDocumentDto Describe(ICustomerSiteSettings site, PendingLegalVersion pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return Describe(site, pending.Kind, pending.VersionId.Value, pending.VersionLabel, pending.EffectiveFrom);
    }
}
