using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Legal.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Legal;
using Khadra.Domain.Legal.Repositories;

namespace Khadra.Application.Legal;

/// <summary>
/// What a registration, an invitation or the consent prompt says the person accepted (Wave 4, W4-8).
/// </summary>
/// <param name="VersionIds">The published versions the screen showed and the person accepted; null or empty for none.</param>
/// <param name="Language">The language of the texts the person read: <c>ar</c> or <c>en</c>.</param>
public sealed record ConsentInput(IReadOnlyCollection<Guid>? VersionIds, string? Language)
{
    public static readonly ConsentInput None = new(null, null);

    /// <summary>Whether a value names one of the two languages, as the validators ask.</summary>
    public static bool IsLanguage(string? value) =>
        Enumeration.GetAll<Language>().Any(language => string.Equals(language.Name, value, StringComparison.OrdinalIgnoreCase));
}

/// <summary>What an account's creation must record of the legal texts, and where (Wave 4, W4-8).</summary>
/// <param name="Required">Whether every text in force must be accepted: yes for every caller but the customer app.</param>
public sealed record ConsentRequest(ConsentInput Input, ConsentChannel Channel, bool Required);

/// <summary>
/// Stages a person's acceptance of the legal texts in force, for the caller's own save (Wave 4, W4-8).
/// </summary>
/// <remarks>
/// <para>
/// <b>Stages, never saves</b> (the advisor's review): the rows ride the save that creates the account, takes up the
/// invitation or answers the prompt, so a refused registration leaves no consent behind and an accepted one never lacks
/// its consent. The same shape as <c>AdminActionRecorder</c> and <c>DocumentAccessRecorder</c>.
/// </para>
/// <para>
/// <b>Only the versions in force.</b> A version that is not — superseded since the screen read it — is refused whole
/// (409 <c>legal.version_not_current</c>), and the screen reloads the texts and asks again. When consent is
/// <c>required</c> and a text is in force, every text in force must be accepted (400 <c>legal.consent_required</c>).
/// With nothing in force, nothing is required and nothing is recorded.
/// </para>
/// <para>
/// <b>A version is recorded once per person</b> (the advisor's review, blocking): an acceptance this person has already
/// given writes nothing. The table is append-only and nothing can empty it, so an endpoint called in a loop must not be
/// able to grow it; this bounds it at people × versions without a unique index, which a later withdrawal and
/// re-acceptance would need absent.
/// </para>
/// </remarks>
public sealed class LegalConsentRecorder(
    ILegalDocumentReader texts,
    ILegalConsentReader consents,
    ILegalConsentRepository repository)
{
    /// <summary>
    /// Where an acceptance was given: the customer app (only a build that declared its version), the website for a
    /// customer, the console for everyone else.
    /// </summary>
    public static ConsentChannel ChannelFor(UserRole role, ClientInfo client)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(client);
        if (client.IsCustomerApp)
            return ConsentChannel.App;
        return role == UserRole.Customer ? ConsentChannel.Website : ConsentChannel.Console;
    }

    /// <returns>How many acceptances were staged.</returns>
    public async Task<Result<int, Error>> StageAsync(
        Id userId,
        ConsentInput input,
        ConsentChannel channel,
        bool required,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(channel);

        var offered = (input.VersionIds ?? []).Select(Id.From).Distinct().ToList();
        if (offered.Count == 0 && !required)
            return 0;

        var inForce = await texts.CurrentAsync(now, cancellationToken);
        var current = inForce.Select(version => version.VersionId).ToHashSet();

        // Every version named must be one in force: an older one is not what this screen should have shown.
        if (offered.Any(versionId => !current.Contains(versionId)))
            return LegalErrors.VersionNotCurrent;

        if (required && current.Any(versionId => !offered.Contains(versionId)))
            return LegalErrors.ConsentRequired;

        if (offered.Count == 0)
            return 0;

        // The validators ask for it first; this keeps the recorder honest for any caller that forgets.
        var language = Enumeration.GetAll<Language>().FirstOrDefault(candidate =>
            string.Equals(candidate.Name, input.Language, StringComparison.OrdinalIgnoreCase));
        if (language is null)
            return LegalErrors.ConsentLanguageRequired;

        var already = await consents.AlreadyAcceptedAsync(userId, offered, cancellationToken);
        var staged = 0;
        foreach (var versionId in offered.Where(versionId => !already.Contains(versionId)))
        {
            repository.Add(LegalConsent.Accept(userId, versionId, channel, language, now));
            staged++;
        }

        return staged;
    }
}
