using Khadra.Application.Common;
using Khadra.Application.Legal;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Legal;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Legal;

/// <summary>
/// The one place a consent is staged (Wave 4, W4-8): a registration, an invitation and the prompt all come through here.
/// </summary>
public sealed class LegalConsentRecorderTests
{
    private static readonly DateTimeOffset Now = TestLegal.Published.AddDays(6);
    private readonly TestLegal _legal = new();
    private readonly Id _userId = Id.New();

    private Task<CSharpFunctionalExtensions.Result<int, Error>> Stage(
        IReadOnlyCollection<Guid>? versionIds,
        string? language = "en",
        bool required = true,
        ConsentChannel? channel = null) =>
        _legal.Recorder.StageAsync(
            _userId, new ConsentInput(versionIds, language), channel ?? ConsentChannel.Website, required, Now, CancellationToken.None);

    [Fact]
    public async Task With_nothing_in_force_nothing_is_required_and_nothing_is_recorded()
    {
        var required = await Stage(null);
        var optional = await Stage(null, required: false);

        Assert.Equal(0, required.Value);
        Assert.Equal(0, optional.Value);
        Assert.Empty(_legal.Staged);
    }

    [Fact]
    public async Task Where_consent_is_required_every_text_in_force_must_be_accepted()
    {
        var (terms, _) = _legal.PublishBoth();

        var none = await Stage(null);
        var onlyTheTerms = await Stage([terms.Value]);

        Assert.Equal(LegalErrors.ConsentRequired, none.Error);
        Assert.Equal(LegalErrors.ConsentRequired, onlyTheTerms.Error);
        Assert.Empty(_legal.Staged);
    }

    [Fact]
    public async Task A_version_that_is_not_in_force_is_refused_whole()
    {
        var (terms, privacy) = _legal.PublishBoth();

        var result = await Stage([terms.Value, privacy.Value, Guid.NewGuid()]);

        Assert.Equal(LegalErrors.VersionNotCurrent, result.Error);
        Assert.Empty(_legal.Staged);
    }

    [Fact]
    public async Task Each_text_in_force_is_recorded_with_where_and_in_which_language_it_was_read()
    {
        var (terms, privacy) = _legal.PublishBoth();

        var result = await Stage([terms.Value, privacy.Value], language: "AR", channel: ConsentChannel.Console);

        Assert.Equal(2, result.Value);
        Assert.Equal([terms, privacy], _legal.Staged.Select(consent => consent.DocumentVersionId));
        Assert.All(_legal.Staged, consent =>
        {
            Assert.Equal(_userId, consent.UserId);
            Assert.Same(ConsentChannel.Console, consent.Channel);
            Assert.Same(Language.Arabic, consent.Language);
            Assert.Equal(Now, consent.OccurredAt);
        });
    }

    /// <summary>
    /// The advisor's review, blocking: the table is append-only and nothing can empty it, so a version already on this
    /// person's record writes nothing — an endpoint called in a loop cannot grow it.
    /// </summary>
    [Fact]
    public async Task A_version_already_accepted_is_never_recorded_twice()
    {
        var (terms, privacy) = _legal.PublishBoth();
        _legal.Consents.AlreadyAcceptedAsync(_userId, Arg.Any<IReadOnlyCollection<Id>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Id> { terms });

        var result = await Stage([terms.Value, privacy.Value, privacy.Value]);

        Assert.Equal(1, result.Value);
        Assert.Equal(privacy, Assert.Single(_legal.Staged).DocumentVersionId);
    }

    [Fact]
    public async Task Where_consent_is_not_required_a_part_may_be_accepted()
    {
        var (_, privacy) = _legal.PublishBoth();

        var result = await Stage([privacy.Value], required: false);

        Assert.Equal(1, result.Value);
        Assert.Equal(privacy, Assert.Single(_legal.Staged).DocumentVersionId);
    }

    [Fact]
    public async Task An_acceptance_must_say_which_language_was_read()
    {
        var (terms, privacy) = _legal.PublishBoth();

        var result = await Stage([terms.Value, privacy.Value], language: "fr");

        Assert.Equal(LegalErrors.ConsentLanguageRequired, result.Error);
        Assert.Empty(_legal.Staged);
    }

    [Fact]
    public void The_channel_is_the_app_for_a_declared_app_build_the_website_for_a_customer_and_the_console_otherwise()
    {
        var app = new ClientInfo("1.2.3.4", "Khadra/1.3.0", AppVersion.Parse("1.3.0"));
        var browser = new ClientInfo("1.2.3.4", "Mozilla/5.0");

        Assert.Same(ConsentChannel.App, LegalConsentRecorder.ChannelFor(UserRole.Customer, app));
        Assert.Same(ConsentChannel.Website, LegalConsentRecorder.ChannelFor(UserRole.Customer, browser));
        Assert.Same(ConsentChannel.Console, LegalConsentRecorder.ChannelFor(UserRole.DealerOwner, browser));
        Assert.Same(ConsentChannel.Console, LegalConsentRecorder.ChannelFor(UserRole.DealerEmployee, browser));
    }
}
