using Khadra.Application.Common;
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
/// The published legal texts on the real repository and reader (Wave 2 G1): every field survives the round trip, the
/// constraints the migration creates hold on SQLite too, and a published version can never be changed.
/// </summary>
public sealed class LegalDocumentPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private static readonly Id AdminId = Id.New();

    public LegalDocumentPersistenceTests()
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

    private static LegalDocumentVersion Version(LegalDocumentKind kind, string label, DateTimeOffset at, string english = "Terms.\n") =>
        LegalDocumentVersion.Publish(kind, label, english, "الشروط.\n", AdminId, at, null).Value;

    private async Task SaveAsync(params LegalDocumentVersion[] versions)
    {
        await using var context = new KhadraDbContext(_options);
        foreach (var version in versions)
            new LegalDocumentVersionRepository(context).Add(version);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Every_field_survives_the_round_trip()
    {
        var published = Version(LegalDocumentKind.Privacy, "2026-10", Build.Now, "# Privacy\n\n\tIndented.\n\U0001F600");
        await SaveAsync(published);

        await using var context = new KhadraDbContext(_options);
        var stored = (await new LegalDocumentVersionRepository(context).GetByIdAsync(published.Id))!;

        Assert.Same(LegalDocumentKind.Privacy, stored.Kind);
        Assert.Equal("2026-10", stored.VersionLabel);
        Assert.Equal(published.EffectiveFrom, stored.EffectiveFrom);
        Assert.Equal(published.PublishedAt, stored.PublishedAt);
        Assert.Equal(AdminId, stored.PublishedByAdminId);
        Assert.Equal(published.BodyEn, stored.BodyEn);
        Assert.Equal(published.BodyAr, stored.BodyAr);
        Assert.Equal(published.BodyEnSha256, stored.BodyEnSha256);
        Assert.Equal(published.BodyArSha256, stored.BodyArSha256);
    }

    [Fact]
    public async Task A_published_version_can_never_be_changed_or_removed()
    {
        var published = Version(LegalDocumentKind.Terms, "2026-10", Build.Now);
        await SaveAsync(published);

        await using (var context = new KhadraDbContext(_options))
        {
            var stored = (await new LegalDocumentVersionRepository(context).GetByIdAsync(published.Id))!;
            context.Entry(stored).Property(version => version.BodyEn).CurrentValue = "Rewritten.";
            await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
        }

        await using (var context = new KhadraDbContext(_options))
        {
            var stored = (await new LegalDocumentVersionRepository(context).GetByIdAsync(published.Id))!;
            context.Remove(stored);
            await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
        }

        await using (var context = new KhadraDbContext(_options))
            Assert.Equal("Terms.\n", (await new LegalDocumentVersionRepository(context).GetByIdAsync(published.Id))!.BodyEn);
    }

    [Fact]
    public async Task One_label_and_one_instant_per_document()
    {
        await SaveAsync(Version(LegalDocumentKind.Terms, "2026-10", Build.Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(Version(LegalDocumentKind.Terms, "2026-10", Build.Now.AddDays(1))));
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(Version(LegalDocumentKind.Terms, "2026-11", Build.Now)));
        // The other document may use both.
        await SaveAsync(Version(LegalDocumentKind.Privacy, "2026-10", Build.Now));
    }

    [Fact]
    public async Task The_database_refuses_a_document_no_build_knows_and_a_version_in_force_before_it_was_published()
    {
        await using var context = new KhadraDbContext(_options);
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(@"
INSERT INTO legal_document_versions (id, kind, version_label, effective_from, published_at, published_by_admin_id,
                                     body_en, body_ar, body_en_sha256, body_ar_sha256)
VALUES ('6f0c8d4e-0000-0000-0000-000000000001', 'Cookies', '1', 2, 2, '6f0c8d4e-0000-0000-0000-000000000002', 'a', 'b',
        '0000000000000000000000000000000000000000000000000000000000000000',
        '0000000000000000000000000000000000000000000000000000000000000000')"));
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(@"
INSERT INTO legal_document_versions (id, kind, version_label, effective_from, published_at, published_by_admin_id,
                                     body_en, body_ar, body_en_sha256, body_ar_sha256)
VALUES ('6f0c8d4e-0000-0000-0000-000000000003', 'Terms', '1', 1, 2, '6f0c8d4e-0000-0000-0000-000000000002', 'a', 'b',
        '0000000000000000000000000000000000000000000000000000000000000000',
        '0000000000000000000000000000000000000000000000000000000000000000')"));
    }

    [Fact]
    public async Task The_version_in_force_is_the_newest_one_already_in_force()
    {
        var september = Version(LegalDocumentKind.Terms, "2026-09", Build.Now.AddDays(-30));
        var october = Version(LegalDocumentKind.Terms, "2026-10", Build.Now.AddDays(-1));
        var privacy = Version(LegalDocumentKind.Privacy, "1.0", Build.Now.AddDays(-2));
        await SaveAsync(september, october, privacy);

        await using var context = new KhadraDbContext(_options);
        var repository = new LegalDocumentVersionRepository(context);
        var reader = new LegalDocumentReader(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<LegalDocumentReader>.Instance);

        Assert.Equal(october.Id, await repository.CurrentIdAsync(LegalDocumentKind.Terms, Build.Now));
        Assert.Equal(september.Id, await repository.CurrentIdAsync(LegalDocumentKind.Terms, Build.Now.AddDays(-2)));
        Assert.Null(await repository.CurrentIdAsync(LegalDocumentKind.Terms, Build.Now.AddDays(-31)));
        Assert.Equal(october.EffectiveFrom, await repository.LatestEffectiveFromAsync(LegalDocumentKind.Terms));
        Assert.True(await repository.LabelTakenAsync(LegalDocumentKind.Terms, "2026-09"));
        Assert.False(await repository.LabelTakenAsync(LegalDocumentKind.Privacy, "2026-09"));

        Assert.Equal(
            [(LegalDocumentKind.Terms, october.Id), (LegalDocumentKind.Privacy, privacy.Id)],
            (await reader.CurrentAsync(Build.Now)).Select(current => (current.Kind, current.VersionId)));

        var listed = await reader.ListAsync(null, PageRequest.From(1, 10));
        Assert.Equal(["2026-10", "1.0", "2026-09"], listed.Items.Select(row => row.VersionLabel));
        Assert.Equal(3, listed.TotalCount);
        // The publisher's account is not in this database: their name does not resolve.
        Assert.All(listed.Items, row => Assert.Null(row.PublishedByName));
        Assert.Equal(["2026-10", "2026-09"], (await reader.ListAsync(LegalDocumentKind.Terms, PageRequest.From(1, 10))).Items.Select(row => row.VersionLabel));
    }
}
