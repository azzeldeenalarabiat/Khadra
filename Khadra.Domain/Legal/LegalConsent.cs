using Khadra.Domain.Common;

namespace Khadra.Domain.Legal;

/// <summary>Where a person accepted a legal text (Wave 4, W4-8).</summary>
/// <remarks>
/// Add-only: every consent names one for good, and the database refuses a name no build knows (a CHECK built from this
/// list), so adding a member changes the model and needs a migration.
/// </remarks>
public sealed class ConsentChannel : Enumeration
{
    /// <summary>The customer website.</summary>
    public static readonly ConsentChannel Website = new(1, "Website");

    /// <summary>The customer app, once a build asks (1.4.0).</summary>
    public static readonly ConsentChannel App = new(2, "App");

    /// <summary>The console: a rental office's owner or staff.</summary>
    public static readonly ConsentChannel Console = new(3, "Console");

    private ConsentChannel(int id, string name) : base(id, name)
    {
    }
}

/// <summary>What a consent row records (Wave 4, W4-8).</summary>
/// <remarks>
/// Add-only, like <see cref="ConsentChannel"/>. A later withdrawal is a new row with its own action, never a change to
/// an acceptance: the acceptance happened, and stays on the record.
/// </remarks>
public sealed class ConsentAction : Enumeration
{
    public static readonly ConsentAction Accepted = new(1, "Accepted");

    private ConsentAction(int id, string name) : base(id, name)
    {
    }
}

/// <summary>
/// One person's acceptance of one published version of a legal text (Wave 4, W4-8; pre-launch item 224).
/// </summary>
/// <remarks>
/// <para>
/// <b>Evidence, so append-only.</b> Never updated and never deleted — the <see cref="IAppendOnly"/> guard in
/// <c>SaveChanges</c> and a database trigger both refuse it — and kept when the account closes (W4-D8, subject to the
/// lawyer's review). The version it names fixes the exact words in both languages, by their hashes on the version, and
/// <see cref="Language"/> says which of the two the person read.
/// </para>
/// <para>
/// <b>Minimal.</b> The person by id alone (IdentityAccess's, with no foreign key across contexts), the version, when,
/// where and in which language. No IP address and no user agent: nothing the record needs, and data to protect.
/// </para>
/// <para>
/// <b>An aggregate of its own, not a child of the user.</b> It must outlive a closed account, and it must never ride the
/// user's own saves, which carry a login or a document upload and would carry these past the append-only guard.
/// </para>
/// <para>
/// <b>Bounded by its writers, not by an index.</b> A person accepts a version once: the recorder writes a row only for a
/// version in force they have not already accepted (the advisor's review), so an endpoint called in a loop cannot fill a
/// table nothing can empty. The table stays non-unique on purpose: a later withdrawal followed by a new acceptance will
/// need a second <see cref="ConsentAction.Accepted"/> row for the same version. So two acceptances sent at the same
/// moment (a double click) can both pass the recorder's check and both be written: the same evidence twice, bounded by
/// the size of the burst, and harmless. Every reader counts acceptances, never rows.
/// </para>
/// </remarks>
public sealed class LegalConsent : AggregateRoot, IAppendOnly
{
    private LegalConsent()
    {
    }

    private LegalConsent(Id id) : base(id)
    {
    }

    /// <summary>The person, by IdentityAccess's id.</summary>
    public Id UserId { get; private set; }

    /// <summary>The published version accepted: the exact texts, in both languages.</summary>
    public Id DocumentVersionId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public ConsentChannel Channel { get; private set; } = null!;

    /// <summary>The language of the text the person read.</summary>
    public Language Language { get; private set; } = null!;

    public ConsentAction Action { get; private set; } = null!;

    /// <summary>Records that <paramref name="userId"/> accepted <paramref name="documentVersionId"/>, read in <paramref name="language"/>.</summary>
    public static LegalConsent Accept(
        Id userId,
        Id documentVersionId,
        ConsentChannel channel,
        Language language,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(language);
        if (userId.IsEmpty)
            throw new DomainException("A consent names the person who gave it.");
        if (documentVersionId.IsEmpty)
            throw new DomainException("A consent names the published version it accepts.");

        return new LegalConsent(Id.New())
        {
            UserId = userId,
            DocumentVersionId = documentVersionId,
            OccurredAt = now.ToUniversalTime(),
            Channel = channel,
            Language = language,
            Action = ConsentAction.Accepted,
        };
    }
}
