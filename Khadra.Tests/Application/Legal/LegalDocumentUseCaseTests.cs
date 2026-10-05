using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Legal;
using Khadra.Application.Legal.Dtos;
using Khadra.Application.Legal.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Legal;
using Khadra.Domain.Legal.Repositories;
using Khadra.Infrastructure.Legal;
using Khadra.Tests.Support;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Khadra.Tests.Application.Legal;

/// <summary>
/// Publishing the legal texts and reading the one in force (Wave 2 G1; pre-launch item 224). Every publish is a new
/// version, audited in its own transaction; the preview writes nothing; the public read is validated by the version.
/// </summary>
public sealed class LegalDocumentUseCaseTests
{
    private static readonly Id AdminId = Id.New();
    private const string English = "# Terms\n\nWelcome to **Khadra**.";
    private const string Arabic = "# الشروط\n\nمرحبًا بك في **خضرا**.";

    private sealed class Context
    {
        public ILegalDocumentVersionRepository Versions { get; } = Substitute.For<ILegalDocumentVersionRepository>();
        public ILegalDocumentReader Reader { get; } = Substitute.For<ILegalDocumentReader>();
        public IAuditTrail AuditTrail { get; } = Substitute.For<IAuditTrail>();
        public ICurrentActor Actor { get; } = Substitute.For<ICurrentActor>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public TestClock Clock { get; } = new(Build.Now);
        public List<LegalDocumentVersion> Added { get; } = [];
        public List<AuditEntry> Audited { get; } = [];

        public Context()
        {
            Actor.UserId.Returns(AdminId);
            Actor.Role.Returns(UserRole.Admin);
            Actor.Name.Returns("Rania Haddad");
            Actor.CorrelationId.Returns("test");
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Versions.When(repository => repository.Add(Arg.Any<LegalDocumentVersion>()))
                .Do(call => Added.Add(call.Arg<LegalDocumentVersion>()));
            AuditTrail.When(trail => trail.Record(Arg.Any<AuditEntry>()))
                .Do(call => Audited.Add(call.Arg<AuditEntry>()));
            Reader.AdminNameAsync(AdminId, Arg.Any<CancellationToken>()).Returns("Rania Haddad");
        }

        /// <summary>A version already published, in force from <paramref name="at"/>.</summary>
        public LegalDocumentVersion GivenInForce(LegalDocumentKind kind, string label, DateTimeOffset at)
        {
            var version = LegalDocumentVersion.Publish(kind, label, English, Arabic, AdminId, at, null).Value;
            Versions.GetByIdAsync(version.Id, Arg.Any<CancellationToken>()).Returns(version);
            Versions.CurrentIdAsync(kind, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(version.Id);
            Versions.LatestEffectiveFromAsync(kind, Arg.Any<CancellationToken>()).Returns(version.EffectiveFrom);
            Versions.LabelTakenAsync(kind, label, Arg.Any<CancellationToken>()).Returns(true);
            return version;
        }

        public LegalDocumentHandlers Handlers() =>
            new(Versions, Reader, new MarkdigLegalTextRenderer(), new AdminActionRecorder(AuditTrail, Actor, Clock), Actor, Clock, UnitOfWork);
    }

    [Fact]
    public async Task Publishing_records_the_version_and_its_audit_entry_in_one_save()
    {
        var context = new Context();
        var previous = context.GivenInForce(LegalDocumentKind.Terms, "2026-09", Build.Now.AddDays(-30));

        var result = await context.Handlers().Handle(
            new PublishLegalDocumentCommand("Terms", " 2026-10 ", English, Arabic), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var version = Assert.Single(context.Added);
        Assert.Equal("2026-10", version.VersionLabel);
        Assert.Equal(Build.Now, version.EffectiveFrom);
        Assert.Equal(AdminId, version.PublishedByAdminId);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var entry = Assert.Single(context.Audited);
        Assert.Same(AuditAction.LegalDocumentPublished, entry.Action);
        Assert.Same(AuditEntityType.LegalDocument, entry.EntityType);
        Assert.Equal(version.Id, entry.EntityId);
        // The kind's name, a code a console words; never an English phrase in a table that cannot be rewritten.
        Assert.Equal("Terms", entry.SubjectLabel);
        Assert.Equal(previous.VersionLabel, entry.PreviousValue);
        Assert.Equal("2026-10", entry.NewValue);
        // The publisher's name is written here, and only here.
        Assert.Equal("Rania Haddad", entry.ActorName);

        Assert.Equal(version.BodyEnSha256, result.Value.Sha256.En);
        Assert.Contains("<strong>Khadra</strong>", result.Value.Html.En, StringComparison.Ordinal);
        Assert.Equal(English, result.Value.Body.En);
    }

    [Fact]
    public async Task The_preview_renders_what_would_be_published_and_writes_nothing()
    {
        var context = new Context();
        var current = context.GivenInForce(LegalDocumentKind.Privacy, "2026-09", Build.Now.AddDays(-1));

        var result = await context.Handlers().Handle(
            new PreviewLegalDocumentQuery("privacy", "2026-10", English, Arabic), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal("Privacy", result.Value.Kind);
        Assert.Equal("<h1>الشروط</h1>\n<p>مرحبًا بك في <strong>خضرا</strong>.</p>\n", result.Value.Html.Ar);
        Assert.Equal(current.Id.Value, result.Value.Replaces!.VersionId);
        Assert.Empty(context.Added);
        Assert.Empty(context.Audited);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_first_version_of_a_document_replaces_nothing()
    {
        var context = new Context();

        var result = await context.Handlers().Handle(
            new PreviewLegalDocumentQuery("Terms", "1.0", English, Arabic), CancellationToken.None);

        Assert.Null(result.Value.Replaces);
    }

    [Fact]
    public async Task Text_outside_the_subset_is_refused_with_its_language_line_and_reason()
    {
        var context = new Context();

        var result = await context.Handlers().Handle(
            new PublishLegalDocumentCommand("Terms", "2026-10", English, "# الشروط\n\n![شعار](https://khadra.jo/l.png)"),
            CancellationToken.None);

        Assert.Equal("legal.text_unsupported", result.Error.Code);
        Assert.Equal("ar", result.Error.Extensions!["language"]);
        Assert.Equal(3, result.Error.Extensions["line"]);
        Assert.Equal("image", result.Error.Extensions["reason"]);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task A_label_already_used_for_the_document_is_refused()
    {
        var context = new Context();
        context.GivenInForce(LegalDocumentKind.Terms, "2026-10", Build.Now.AddDays(-1));

        var result = await context.Handlers().Handle(
            new PublishLegalDocumentCommand("Terms", "2026-10", English, Arabic), CancellationToken.None);

        Assert.Equal("legal.label_taken", result.Error.Code);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task An_unknown_document_is_refused()
    {
        var context = new Context();

        var result = await context.Handlers().Handle(
            new PublishLegalDocumentCommand("Cookies", "1.0", English, Arabic), CancellationToken.None);

        Assert.Equal("legal.kind_unknown", result.Error.Code);
    }

    /// <summary>Two administrators publishing at once: the unique indexes settle it, and the loser is told which.</summary>
    [Theory]
    [InlineData(ILegalDocumentVersionRepository.KindLabelIndex, "legal.label_taken")]
    [InlineData(ILegalDocumentVersionRepository.KindEffectiveFromIndex, "legal.publish_conflict")]
    public async Task A_publish_that_loses_a_race_is_told_why(string index, string expected)
    {
        var context = new Context();
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new UniqueConstraintConflictException("taken", index, new InvalidOperationException()));

        var result = await context.Handlers().Handle(
            new PublishLegalDocumentCommand("Terms", "2026-10", English, Arabic), CancellationToken.None);

        Assert.Equal(expected, result.Error.Code);
    }

    // ── The version in force, as anyone reads it ────────────────────────────────────────────────

    [Fact]
    public async Task A_document_with_nothing_published_says_so()
    {
        var context = new Context();

        var result = await context.Handlers().Handle(new GetCurrentLegalDocumentQuery("terms", null), CancellationToken.None);

        Assert.Equal("legal.not_published", result.Error.Code);
    }

    [Fact]
    public async Task The_version_in_force_is_served_with_a_validator_naming_it_and_the_renderer()
    {
        var context = new Context();
        var version = context.GivenInForce(LegalDocumentKind.Terms, "2026-10", Build.Now.AddDays(-1));

        var result = await context.Handlers().Handle(new GetCurrentLegalDocumentQuery("terms", null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal($"W/\"legal-r1-{version.Id.Value:N}\"", result.Value.ETag);
        var document = result.Value.Document!;
        Assert.Equal(version.Id.Value, document.VersionId);
        Assert.Equal("2026-10", document.VersionLabel);
        Assert.Contains("<h1>Terms</h1>", document.Html.En, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("\"other\", {0}")]
    [InlineData("*")]
    public async Task A_caller_holding_the_version_in_force_is_sent_nothing_more(string ifNoneMatch)
    {
        var context = new Context();
        var version = context.GivenInForce(LegalDocumentKind.Terms, "2026-10", Build.Now.AddDays(-1));
        var etag = $"W/\"legal-r1-{version.Id.Value:N}\"";

        var result = await context.Handlers().Handle(
            new GetCurrentLegalDocumentQuery("terms", string.Format(System.Globalization.CultureInfo.InvariantCulture, ifNoneMatch, etag)),
            CancellationToken.None);

        Assert.Equal(etag, result.Value.ETag);
        Assert.Null(result.Value.Document);
        // Answered from the id alone: the texts are never loaded for a copy that is still current.
        await context.Versions.DidNotReceive().GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_caller_holding_an_older_version_is_sent_the_new_one()
    {
        var context = new Context();
        context.GivenInForce(LegalDocumentKind.Terms, "2026-10", Build.Now.AddDays(-1));

        var result = await context.Handlers().Handle(
            new GetCurrentLegalDocumentQuery("terms", $"W/\"legal-r1-{Guid.NewGuid():N}\""), CancellationToken.None);

        Assert.NotNull(result.Value.Document);
    }

    // ── The administrator's list ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_says_which_version_is_in_force_and_which_were_replaced()
    {
        var context = new Context();
        var older = new LegalVersionRow(Id.New(), LegalDocumentKind.Terms, "2026-09", Build.Now.AddDays(-30), Build.Now.AddDays(-30), AdminId, "Rania Haddad");
        var newer = new LegalVersionRow(Id.New(), LegalDocumentKind.Terms, "2026-10", Build.Now.AddDays(-1), Build.Now.AddDays(-1), AdminId, null);
        context.Reader.ListAsync(null, Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<LegalVersionRow>([newer, older], 1, 20, 2));
        context.Reader.CurrentAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new CurrentLegalVersion(LegalDocumentKind.Terms, newer.VersionId, newer.VersionLabel, newer.EffectiveFrom)]);

        var result = await context.Handlers().Handle(new ListLegalDocumentVersionsQuery(null, null, null), CancellationToken.None);

        Assert.Equal(
            [("2026-10", LegalVersionStates.Current, (string?)null), ("2026-09", LegalVersionStates.Superseded, "Rania Haddad")],
            result.Value.Items.Select(item => (item.VersionLabel, item.State, item.PublishedByName)));
    }
}
