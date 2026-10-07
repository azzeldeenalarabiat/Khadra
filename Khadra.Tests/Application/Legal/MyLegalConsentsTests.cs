using Khadra.Application.Common;
using Khadra.Application.Legal;
using Khadra.Application.Legal.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Legal;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Legal;

/// <summary>
/// A person's own consents, and the prompt's answer (Wave 4, W4-8): <c>GET</c> and <c>POST /auth/me/legal-consents</c>.
/// </summary>
public sealed class MyLegalConsentsTests
{
    private static readonly DateTimeOffset Now = TestLegal.Published.AddDays(6);
    private static readonly ClientInfo Browser = new("1.2.3.4", "Mozilla/5.0");

    private readonly TestLegal _legal = new();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Id _userId = Id.New();

    private MyLegalConsentsHandlers Handlers() =>
        new(_legal.Consents, _legal.Recorder, _legal.Site, new TestClock(Now), _unitOfWork);

    private void Pending(params PendingLegalVersion[] pending) =>
        _legal.Consents.PendingAsync(_userId, Now, Arg.Any<CancellationToken>()).Returns(pending);

    [Fact]
    public async Task The_record_lists_every_acceptance_and_every_text_still_to_accept_with_its_pages()
    {
        var terms = new PendingLegalVersion(LegalDocumentKind.Terms, Id.New(), "2026-10", TestLegal.Published);
        Pending(terms);
        _legal.Consents.AcceptedAsync(_userId, Arg.Any<CancellationToken>()).Returns(
        [
            new AcceptedLegalVersion(LegalDocumentKind.Privacy, Id.New(), "2026-09", Now.AddDays(-1), ConsentChannel.Website, Language.Arabic),
        ]);

        var record = (await Handlers().Handle(new GetMyLegalConsentsQuery(_userId, UserRole.Customer), CancellationToken.None)).Value;

        var accepted = Assert.Single(record.Accepted);
        Assert.Equal(("Privacy", "2026-09", "Website", "ar"), (accepted.Kind, accepted.VersionLabel, accepted.Channel, accepted.Language));
        var pending = Assert.Single(record.Pending);
        Assert.Equal(("Terms", "terms", terms.VersionId.Value), (pending.Kind, pending.Slug, pending.VersionId));
        Assert.Equal("https://khadra.test/en/terms", pending.PageUrls!.En);
        Assert.Equal("https://khadra.test/ar/terms", pending.PageUrls.Ar);
    }

    [Fact]
    public async Task An_administrator_is_never_asked()
    {
        Pending(new PendingLegalVersion(LegalDocumentKind.Terms, Id.New(), "2026-10", TestLegal.Published));

        var record = (await Handlers().Handle(new GetMyLegalConsentsQuery(_userId, UserRole.Admin), CancellationToken.None)).Value;

        Assert.Empty(record.Pending);
    }

    [Fact]
    public async Task Accepting_records_the_texts_in_one_save_and_answers_with_the_record()
    {
        var (terms, privacy) = _legal.PublishBoth();

        var result = await Handlers().Handle(
            new AcceptLegalTextsCommand(_userId, UserRole.Customer, [terms.Value, privacy.Value], "en", Browser),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _legal.Staged.Count);
        Assert.All(_legal.Staged, consent => Assert.Same(ConsentChannel.Website, consent.Channel));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accepting_again_writes_nothing()
    {
        var (terms, privacy) = _legal.PublishBoth();
        _legal.Consents.AlreadyAcceptedAsync(_userId, Arg.Any<IReadOnlyCollection<Id>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Id> { terms, privacy });

        var result = await Handlers().Handle(
            new AcceptLegalTextsCommand(_userId, UserRole.DealerOwner, [terms.Value, privacy.Value], "ar", Browser),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_legal.Staged);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_text_no_longer_in_force_is_refused_and_nothing_is_saved()
    {
        _legal.PublishBoth();

        var result = await Handlers().Handle(
            new AcceptLegalTextsCommand(_userId, UserRole.Customer, [Guid.NewGuid()], "en", Browser),
            CancellationToken.None);

        Assert.Equal(LegalErrors.VersionNotCurrent, result.Error);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The advisor's review: a row saying staff agreed to the customers' terms would be a permanent untruth.</summary>
    [Fact]
    public async Task An_administrators_acceptance_records_nothing()
    {
        var (terms, privacy) = _legal.PublishBoth();

        var result = await Handlers().Handle(
            new AcceptLegalTextsCommand(_userId, UserRole.Admin, [terms.Value, privacy.Value], "en", Browser),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_legal.Staged);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void An_acceptance_names_at_least_one_text_and_a_language()
    {
        var validator = new AcceptLegalTextsCommandValidator();

        Assert.False(validator.Validate(new AcceptLegalTextsCommand(_userId, UserRole.Customer, [], "en", Browser)).IsValid);
        Assert.False(validator.Validate(new AcceptLegalTextsCommand(_userId, UserRole.Customer, [Guid.NewGuid()], "fr", Browser)).IsValid);
        Assert.True(validator.Validate(new AcceptLegalTextsCommand(_userId, UserRole.Customer, [Guid.NewGuid()], "ar", Browser)).IsValid);
    }
}
