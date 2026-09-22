using Khadra.Application.Common;

namespace Khadra.Infrastructure.Configuration;

/// <summary>
/// Configuration section <c>MobileApp</c>: the oldest customer-app build this API serves.
/// </summary>
/// <remarks>
/// Configuration, not code, because raising it is a RELEASE decision that has to move with the API
/// build that breaks the old contract — and an operator may need to lower it again without a
/// deployment if a new build turns out not to be in customers' hands yet.
/// </remarks>
public sealed class MobileAppOptions
{
    public const string SectionName = "MobileApp";

    /// <summary>
    /// A Semantic Version release such as <c>1.1.0</c>. Empty means no build is refused.
    /// </summary>
    /// <remarks>
    /// A prerelease is refused here: a minimum of <c>1.1.0-rc.1</c> would admit every release
    /// candidate after it and read to a person as though it meant 1.1.0.
    /// </remarks>
    public string? MinimumSupportedVersion { get; init; }

    /// <summary>Where customers download the current build. Optional; absolute http(s).</summary>
    public string? UpdateUrl { get; init; }

    internal AppVersion? ParsedMinimum =>
        string.IsNullOrWhiteSpace(MinimumSupportedVersion)
            ? null
            : AppVersion.TryParse(MinimumSupportedVersion.Trim(), out var version) ? version : null;

    internal Uri? ParsedUpdateUrl =>
        Uri.TryCreate(UpdateUrl?.Trim(), UriKind.Absolute, out var url)
        && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            ? url
            : null;

    /// <summary>A minimum that is set must parse, and must be a release.</summary>
    internal bool MinimumIsValid =>
        string.IsNullOrWhiteSpace(MinimumSupportedVersion)
        || ParsedMinimum is { IsPrerelease: false };

    /// <summary>A link that is set must be one a phone can open.</summary>
    internal bool UpdateUrlIsValid => string.IsNullOrWhiteSpace(UpdateUrl) || ParsedUpdateUrl is not null;
}
