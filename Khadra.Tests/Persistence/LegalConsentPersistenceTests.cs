using Khadra.Application.Legal.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Legal;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Consents to the legal texts on the real repository and reader (Wave 4, W4-8): every field survives, a consent can
/// never be changed or removed, the database refuses what no build knows, and "pending" is read in one statement.
/// </summary>
public sealed class LegalConsentPersistenceTests : IDisposable
{
    private static readonly Id AdminId = Id.New();
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly Id _userId = Id.New();

    public LegalConsentPersistenceTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private async Task<LegalDocumentVersion> PublishAsync(LegalDocumentKind kind, string label, DateTimeOffset at)
    {
        var version = LegalDocumentVersion.Publish(kind, label, "Text.\n", "نص.\n", AdminId, at, null).Value;
        await using var context = new KhadraDbContext(_options);
        new LegalDocumentVersionRepository(context).Add(version);
        await context.SaveChangesAsync();
        return version;
    }

    private async Task<LegalConsent> AcceptAsync(
        LegalDocumentVersion version,
        DateTimeOffset at,
        ConsentChannel? channel = null,
        Language? language = null)
    {
        var consent = LegalConsent.Accept(_userId, version.Id, channel ?? ConsentChannel.Website, language ?? Language.English, at);
        await using var context = new KhadraDbContext(_options);
        new LegalConsentRepository(context).Add(consent);
        await context.SaveChangesAsync();
        return consent;
    }

    private async Task<IReadOnlyList<PendingLegalVersion>> PendingAsync(DateTimeOffset at)
    {
        await using var context = new KhadraDbContext(_options);
        return await new LegalConsentReader(context).PendingAsync(_userId, at);
    }

    [Fact]
    public async Task Every_field_survives_the_round_trip()
    {
        var terms = await PublishAsync(LegalDocumentKind.Terms, "2026-10", Build.Now);
        var consent = await AcceptAsync(terms, Build.Now.AddMinutes(5), ConsentChannel.Console, Language.Arabic);

        await using var context = new KhadraDbContext(_options);
        var stored = await context.LegalConsents.AsNoTracking().SingleAsync();

        Assert.Equal(consent.Id, stored.Id);
        Assert.Equal(_userId, stored.UserId);
        Assert.Equal(terms.Id, stored.DocumentVersionId);
        Assert.Equal(Build.Now.AddMinutes(5), stored.OccurredAt);
        Assert.Same(ConsentChannel.Console, stored.Channel);
        Assert.Same(Language.Arabic, stored.Language);
        Assert.Same(ConsentAction.Accepted, stored.Action);
    }

    [Fact]
    public async Task A_consent_can_never_be_changed_or_removed()
    {
        var terms = await PublishAsync(LegalDocumentKind.Terms, "2026-10", Build.Now);
        var consent = await AcceptAsync(terms, Build.Now);

        await using (var context = new KhadraDbContext(_options))
        {
            var stored = await context.LegalConsents.SingleAsync(row => row.Id == consent.Id);
            context.Entry(stored).Property(row => row.OccurredAt).CurrentValue = Build.Now.AddDays(-30);
            await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
        }

        await using (var context = new KhadraDbContext(_options))
        {
            context.Remove(await context.LegalConsents.SingleAsync(row => row.Id == consent.Id));
            await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
        }

        await using (var context = new KhadraDbContext(_options))
            Assert.Equal(Build.Now, (await context.LegalConsents.AsNoTracking().SingleAsync()).OccurredAt);
    }

    [Fact]
    public async Task A_consent_names_a_version_that_exists_and_only_values_a_build_knows()
    {
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await using var context = new KhadraDbContext(_options);
            new LegalConsentRepository(context).Add(LegalConsent.Accept(_userId, Id.New(), ConsentChannel.Website, Language.English, Build.Now));
            await context.SaveChangesAsync();
        });

        var terms = await PublishAsync(LegalDocumentKind.Terms, "2026-10", Build.Now);
        await using var raw = new KhadraDbContext(_options);
        foreach (var (channel, language, action) in new[] { ("Fax", "en", "Accepted"), ("Website", "fr", "Accepted"), ("Website", "en", "Withdrawn") })
        {
            await Assert.ThrowsAsync<SqliteException>(() => raw.Database.ExecuteSqlAsync($@"
INSERT INTO legal_consents (id, user_id, document_version_id, occurred_at, channel, language, action)
VALUES ({Guid.NewGuid()}, {_userId.Value}, {terms.Id.Value}, 1, {channel}, {language}, {action})"));
        }
    }

    [Fact]
    public async Task With_nothing_published_nothing_is_pending()
    {
        Assert.Empty(await PendingAsync(Build.Now));
    }

    /// <summary>The one statement the gate, <c>/auth/me</c> and the prompt all read.</summary>
    [Fact]
    public async Task Pending_is_every_text_in_force_not_yet_accepted_in_the_kinds_order()
    {
        var privacy = await PublishAsync(LegalDocumentKind.Privacy, "1.0", Build.Now.AddDays(-2));
        var terms = await PublishAsync(LegalDocumentKind.Terms, "2026-10", Build.Now.AddDays(-1));

        var before = await PendingAsync(Build.Now);
        await AcceptAsync(terms, Build.Now);
        var afterTerms = await PendingAsync(Build.Now);
        await AcceptAsync(privacy, Build.Now);
        var afterBoth = await PendingAsync(Build.Now);

        Assert.Equal([terms.Id, privacy.Id], before.Select(row => row.VersionId));
        Assert.Equal("2026-10", before[0].VersionLabel);
        Assert.Equal(privacy.Id, Assert.Single(afterTerms).VersionId);
        Assert.Empty(afterBoth);
    }

    [Fact]
    public async Task A_newer_version_asks_again_and_an_older_one_no_longer_counts()
    {
        var september = await PublishAsync(LegalDocumentKind.Terms, "2026-09", Build.Now.AddDays(-30));
        await AcceptAsync(september, Build.Now.AddDays(-29));
        var october = await PublishAsync(LegalDocumentKind.Terms, "2026-10", Build.Now.AddDays(-1));

        var now = await PendingAsync(Build.Now);
        var beforeOctober = await PendingAsync(Build.Now.AddDays(-2));

        Assert.Equal(october.Id, Assert.Single(now).VersionId);
        // Read as of an instant before October was published, September was the one in force, and it was accepted.
        Assert.Empty(beforeOctober);
    }

    [Fact]
    public async Task The_record_is_newest_first_and_says_what_was_accepted_where_and_in_which_language()
    {
        var terms = await PublishAsync(LegalDocumentKind.Terms, "2026-10", Build.Now.AddDays(-3));
        var privacy = await PublishAsync(LegalDocumentKind.Privacy, "1.0", Build.Now.AddDays(-3));
        await AcceptAsync(terms, Build.Now.AddDays(-2), ConsentChannel.Website, Language.Arabic);
        await AcceptAsync(privacy, Build.Now.AddDays(-1), ConsentChannel.App, Language.English);

        await using var context = new KhadraDbContext(_options);
        var reader = new LegalConsentReader(context);
        var record = await reader.AcceptedAsync(_userId);
        var already = await reader.AlreadyAcceptedAsync(_userId, [terms.Id, Id.New()]);

        Assert.Equal(["1.0", "2026-10"], record.Select(row => row.VersionLabel));
        Assert.Equal((LegalDocumentKind.Privacy, ConsentChannel.App, Language.English), (record[0].Kind, record[0].Channel, record[0].Language));
        Assert.Equal(Build.Now.AddDays(-2), record[1].AcceptedAt);
        Assert.Equal([terms.Id], already);
    }
}
