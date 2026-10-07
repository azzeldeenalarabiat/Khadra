using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSubstitute;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// An administrator's access to a renter's document on a real PostgreSQL (Wave 4, W4-9), opt-in like every
/// scratch-database proof: the scope rule under the name the code relies on, the document's own concurrency token in
/// both orders of the race it exists for, and a rollback that refuses once an administrator has looked.
/// </summary>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresAdminDocumentAccessTests
{
    private const string Previous = "20261007025537_LegalConsents";

    private static readonly DateTimeOffset Now = Build.Now;

    private static DocumentAccessEntry AdminView() =>
        DocumentAccessEntry.RecordAdminView(
            Id.New(), "Dana Saleh", Id.New(), Id.New(), CustomerDocumentType.Passport, Now.AddDays(-1), Now);

    private static DocumentAccessEntry OfficeView() =>
        DocumentAccessEntry.Record(
            DocumentAccessAction.Viewed, Id.New(), "Rami Haddad", UserRole.DealerOwner, Id.New(), Id.New(), Id.New(), Id.New(),
            CustomerDocumentType.Passport, Now.AddDays(-1), Now);

    private static async Task RecordAsync(Scratch database, DocumentAccessEntry entry)
    {
        await using var context = new KhadraDbContext(database.Options);
        context.DocumentAccessEntries.Add(entry);
        await context.SaveChangesAsync();
    }

    [PostgresFact]
    public async Task Only_an_administrators_view_may_name_no_dealership_and_no_booking()
    {
        var database = await FreshDatabaseAsync("adminview");
        var view = AdminView();
        await RecordAsync(database, view);

        await using var connection = await OpenAsync(database);
        await using (var stored = new NpgsqlCommand(
            $"SELECT dealer_id IS NULL AND booking_id IS NULL AND actor_role = 'Admin' FROM document_access_entries WHERE id = '{view.Id.Value}'",
            connection))
            Assert.True((bool)(await stored.ExecuteScalarAsync())!);

        var insert = "INSERT INTO document_access_entries (id, occurred_at, actor_user_id, actor_name, actor_role, dealer_id, booking_id, " +
                     "subject_user_id, document_id, document_type, document_uploaded_at, action) VALUES (gen_random_uuid(), now(), " +
                     "gen_random_uuid(), 'Rami Haddad', 'DealerOwner', ";
        await CheckRefusedAsync(connection, insert + "gen_random_uuid(), NULL, gen_random_uuid(), gen_random_uuid(), 'Passport', now(), 'Viewed')");
        await CheckRefusedAsync(connection, insert + "NULL, gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 'Passport', now(), 'Viewed')");

        // Still append-only: the triggers refuse rows, whoever wrote them.
        await using var edit = new NpgsqlCommand($"UPDATE document_access_entries SET actor_name = 'x' WHERE id = '{view.Id.Value}'", connection);
        await Assert.ThrowsAsync<PostgresException>(() => edit.ExecuteNonQueryAsync());
    }

    /// <summary>
    /// A document row is a slot whose file is replaced in place, so the rejection and the upload write the SAME row. The
    /// token makes the second writer lose, in either order: a rejection never lands on a file nobody opened, and an
    /// upload never silently undoes a rejection it did not see (its handler reads afresh and applies the file again).
    /// </summary>
    [PostgresFact]
    public async Task A_rejection_and_a_new_upload_on_one_document_cannot_both_win()
    {
        var database = await FreshDatabaseAsync("docrace");
        var customer = Build.Customer(Now);
        await using (var seed = new KhadraDbContext(database.Options))
        {
            seed.Users.Add(customer);
            await seed.SaveChangesAsync();
        }

        // The upload commits first: the administrator's rejection, judged on the file they opened, is refused.
        await using (var admin = new KhadraDbContext(database.Options))
        {
            var held = await new UserRepository(admin).GetByIdAsync(customer.Id);
            var front = held!.Documents.Single(document => document.Type == CustomerDocumentType.DrivingLicenceFront);

            await using (var upload = new KhadraDbContext(database.Options))
            {
                var account = await new UserRepository(upload).GetByIdAsync(customer.Id);
                account!.AttachDocument(CustomerDocumentType.DrivingLicenceFront, "customers/new-front.jpg", "image/jpeg", 10, Now.AddMinutes(5));
                await upload.SaveChangesAsync();
            }

            Assert.True(held.RejectDocument(front.Id, "Blurred.", front.UploadedAt).IsSuccess);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => UnitOfWork(admin).SaveChangesAsync());
        }

        // The rejection commits first: the upload that loaded before it is refused, and is the one that tries again.
        await using (var upload = new KhadraDbContext(database.Options))
        {
            var held = await new UserRepository(upload).GetByIdAsync(customer.Id);

            await using (var admin = new KhadraDbContext(database.Options))
            {
                var account = await new UserRepository(admin).GetByIdAsync(customer.Id);
                var back = account!.Documents.Single(document => document.Type == CustomerDocumentType.DrivingLicenceBack);
                Assert.True(account.RejectDocument(back.Id, "Expired licence.", back.UploadedAt).IsSuccess);
                await admin.SaveChangesAsync();
            }

            held!.AttachDocument(CustomerDocumentType.DrivingLicenceBack, "customers/new-back.jpg", "image/jpeg", 10, Now.AddMinutes(6));
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => UnitOfWork(upload).SaveChangesAsync());
        }

        await using var read = new KhadraDbContext(database.Options);
        var stored = (await new UserRepository(read).GetByIdAsync(customer.Id))!.Documents;
        var storedFront = stored.Single(document => document.Type == CustomerDocumentType.DrivingLicenceFront);
        var storedBack = stored.Single(document => document.Type == CustomerDocumentType.DrivingLicenceBack);
        Assert.Equal("customers/new-front.jpg", storedFront.StorageKey);
        Assert.Equal(CustomerDocumentStatus.PendingReview, storedFront.Status);
        Assert.Null(storedFront.ReviewNote);
        Assert.Equal(CustomerDocumentStatus.Rejected, storedBack.Status);
        Assert.Equal("Expired licence.", storedBack.ReviewNote);
    }

    [PostgresFact]
    public async Task The_rollback_runs_while_only_offices_have_looked_and_refuses_once_an_administrator_has()
    {
        var database = await FreshDatabaseAsync("adminviewdown");
        await RecordAsync(database, OfficeView());

        await using (var context = new KhadraDbContext(database.Options))
            await context.GetService<IMigrator>().MigrateAsync(Previous);
        await using (var connection = await OpenAsync(database))
        await using (var required = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'document_access_entries' " +
            "AND column_name IN ('dealer_id', 'booking_id') AND is_nullable = 'NO'", connection))
            Assert.Equal(2L, (long)(await required.ExecuteScalarAsync())!);

        await using (var context = new KhadraDbContext(database.Options))
            await context.Database.MigrateAsync();
        await RecordAsync(database, AdminView());

        await using (var context = new KhadraDbContext(database.Options))
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(Previous));
            Assert.Contains("fixed forward", refusal.MessageText, StringComparison.Ordinal);
        }

        await using (var connection = await OpenAsync(database))
        await using (var rows = new NpgsqlCommand("SELECT count(*) FROM document_access_entries", connection))
            Assert.Equal(2L, (long)(await rows.ExecuteScalarAsync())!);
    }

    // ── Helpers, as the other scratch-database proofs have them ──────────────────────────────────

    private static UnitOfWork UnitOfWork(KhadraDbContext context) => new(context, Substitute.For<IDomainEventDispatcher>());

    private static async Task CheckRefusedAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_document_access_entries_scope", refusal.ConstraintName);
    }

    private static async Task<NpgsqlConnection> OpenAsync(Scratch database)
    {
        var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private sealed record Scratch(DbContextOptions<KhadraDbContext> Options, string ConnectionString);

    /// <summary>A database nothing else has used, named after the configured scratch one, migrated to the latest.</summary>
    private static async Task<Scratch> FreshDatabaseAsync(string scenario)
    {
        var configured = PostgresTestDatabase.ConnectionString
            ?? throw new InvalidOperationException($"{PostgresTestDatabase.VariableName} is not set.");
        var fresh = new NpgsqlConnectionStringBuilder(configured);
        var name = $"{fresh.Database}_{scenario}_{Guid.NewGuid():N}";
        fresh.Database = name[..Math.Min(63, $"{fresh.Database}_{scenario}_".Length + 8)];

        var maintenance = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(maintenance.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE \"{fresh.Database!.Replace("\"", "\"\"", StringComparison.Ordinal)}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(fresh.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new KhadraDbContext(options);
        await context.Database.MigrateAsync();
        return new Scratch(options, fresh.ConnectionString);
    }
}
