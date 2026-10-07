using Khadra.Domain.Common;
using Khadra.Domain.Legal;

namespace Khadra.Tests.Domain.Legal;

/// <summary>One person's acceptance of one published version of a legal text (Wave 4, W4-8).</summary>
public sealed class LegalConsentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 30, 0, TimeSpan.FromHours(3));

    [Fact]
    public void An_acceptance_records_who_which_version_when_where_and_in_which_language()
    {
        var userId = Id.New();
        var versionId = Id.New();

        var consent = LegalConsent.Accept(userId, versionId, ConsentChannel.Website, Language.Arabic, Now);

        Assert.Equal(userId, consent.UserId);
        Assert.Equal(versionId, consent.DocumentVersionId);
        Assert.Same(ConsentChannel.Website, consent.Channel);
        Assert.Same(Language.Arabic, consent.Language);
        Assert.Same(ConsentAction.Accepted, consent.Action);
        // Stored in UTC, the same instant.
        Assert.Equal(TimeSpan.Zero, consent.OccurredAt.Offset);
        Assert.Equal(Now, consent.OccurredAt);
        Assert.False(consent.Id.IsEmpty);
    }

    [Fact]
    public void An_acceptance_always_names_the_person_and_the_version()
    {
        Assert.Throws<DomainException>(() => LegalConsent.Accept(Id.Empty, Id.New(), ConsentChannel.Console, Language.English, Now));
        Assert.Throws<DomainException>(() => LegalConsent.Accept(Id.New(), Id.Empty, ConsentChannel.Console, Language.English, Now));
    }

    [Fact]
    public void A_consent_is_append_only()
    {
        Assert.IsAssignableFrom<IAppendOnly>(LegalConsent.Accept(Id.New(), Id.New(), ConsentChannel.App, Language.English, Now));
    }

    /// <summary>
    /// The names are stored for good and the database's CHECKs are built from them, so they are pinned: renaming one
    /// would orphan every row that carries it.
    /// </summary>
    [Fact]
    public void The_channels_and_actions_keep_the_names_they_are_stored_by()
    {
        Assert.Equal(["Website", "App", "Console"], Enumeration.GetAll<ConsentChannel>().Select(channel => channel.Name));
        Assert.Equal(["Accepted"], Enumeration.GetAll<ConsentAction>().Select(action => action.Name));
    }
}
