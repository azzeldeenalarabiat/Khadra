using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// What PostgreSQL actually says when a unique index refuses a write, and what we turn it into.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this cannot be a SQLite test.</b> The rest of the persistence suite runs on SQLite and is
/// right to: the mapping, the round-trip and the aggregate rules are engine-independent. This is not.
/// The translation in <see cref="UnitOfWork"/> keys on two PostgreSQL facts — SQLSTATE <c>23505</c>
/// and the CONSTRAINT NAME the server reports — and both are things only the real engine can be
/// asked. SQLite raises a different exception type with a different message and no constraint name
/// at all, so a SQLite test of this would pass while proving the opposite of what it claimed.
/// </para>
/// <para>
/// <b>Why the name matters as much as the code.</b> <c>23505</c> alone says "some unique index
/// refused this", which is not enough: the replay handling in <c>ReceiveProviderEventHandler</c>
/// swallows the conflict and answers 2xx, and swallowing the WRONG unique violation would turn a
/// genuine data error into a silent success on a money endpoint. So the exception carries the name,
/// and the handler checks it. Both halves are exercised below — a duplicate provider event, and a
/// duplicate on a different unique index that must NOT be mistaken for one.
/// </para>
/// <para>
/// <b>Opt-in.</b> These are <see cref="PostgresFactAttribute"/>, skipped unless
/// <c>KHADRA_TEST_POSTGRES</c> names a scratch database. See that attribute for why it is not a
/// probe of localhost.
/// </para>
/// </remarks>
public sealed class PostgresConstraintTranslationTests : IAsyncLifetime, IDisposable
{
    private const string Provider = "PGCHECK";

    private KhadraDbContext? _context;
    private UnitOfWork _unitOfWork = null!;

    public async Task InitializeAsync()
    {
        var connectionString = PostgresTestDatabase.ConnectionString;
        if (connectionString is null) return;

        await EnsureDatabaseExistsAsync(connectionString);

        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        _context = new KhadraDbContext(options);
        // The real migrations, not EnsureCreated: the schema under test includes an exclusion
        // constraint and the extension behind it, which only a migration puts there.
        await _context.Database.MigrateAsync();
        _unitOfWork = new UnitOfWork(_context, Substitute.For<IDomainEventDispatcher>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _context?.Dispose();

    /// <summary>The context, once <see cref="InitializeAsync"/> has built one.</summary>
    /// <remarks>
    /// A property rather than the field, so that a skipped run — where nothing was built — fails
    /// loudly here instead of with a null reference several lines into a test.
    /// </remarks>
    private KhadraDbContext Context => _context
        ?? throw new InvalidOperationException($"{PostgresTestDatabase.VariableName} is not set.");

    /// <summary>
    /// Creates the scratch database if it is not there. It is never dropped.
    /// </summary>
    /// <remarks>
    /// Dropping a database from a test is one mistyped variable away from dropping the wrong one, and
    /// this suite has already learned that lesson once — a plain <c>dotnet test</c> migrated a
    /// developer's real database on 2026-09-17. So the database persists between runs and the rows
    /// carry unique ids instead.
    /// </remarks>
    private static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = target.Database
            ?? throw new InvalidOperationException($"{PostgresTestDatabase.VariableName} names no database.");

        var maintenance = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();

        await using var exists = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", databaseName);
        if (await exists.ExecuteScalarAsync() is not null) return;

        // The name comes from the developer's own environment variable and cannot be parameterised
        // in DDL, so it is quoted as an identifier rather than interpolated raw.
        await using var create = new NpgsqlCommand(
            $"CREATE DATABASE \"{databaseName.Replace("\"", "\"\"", StringComparison.Ordinal)}\"",
            connection);
        await create.ExecuteNonQueryAsync();
    }

    private static ProviderEventReceipt Receipt(string eventId, string? reference = null) =>
        ProviderEventReceipt.Record(
            Provider,
            eventId,
            reference,
            "captured",
            paymentId: null,
            ProviderEventOutcome.Unknown,
            amount: null,
            Build.Now);

    /// <summary>
    /// A second delivery of the same event is refused by the index, as SQLSTATE 23505, and arrives
    /// at the handler as a named conflict rather than as an anonymous <c>DbUpdateException</c>.
    /// </summary>
    /// <remarks>
    /// This is the fact the webhook's whole replay story rests on. Before the translation existed, a
    /// duplicate delivery surfaced as a bare <c>DbUpdateException</c>, the API mapped it to 409, and
    /// every provider reads 409 as "retry" — so a provider catching up after an outage would offer
    /// the same event until it disabled the endpoint. Nothing was ever double-applied; the index saw
    /// to that. It was the ANSWER that was wrong.
    /// </remarks>
    [PostgresFact]
    public async Task A_duplicate_provider_event_is_translated_with_its_constraint_name()
    {
        var eventId = PostgresTestDatabase.Unique("evt");

        Context.Set<ProviderEventReceipt>().Add(Receipt(eventId));
        await _unitOfWork.SaveChangesAsync();

        // A second receipt for the same delivery, exactly as a retried webhook would produce.
        Context.Set<ProviderEventReceipt>().Add(Receipt(eventId));

        var conflict = await Assert.ThrowsAsync<UniqueConstraintConflictException>(
            () => _unitOfWork.SaveChangesAsync());

        Assert.Equal(
            UniqueConstraintConflictException.ProviderEventReceiptConstraint,
            conflict.ConstraintName);
        Assert.True(conflict.IsProviderEventReceipt);
    }

    /// <summary>
    /// The underlying SQLSTATE really is 23505, stated once so the translation's premise is on record.
    /// </summary>
    [PostgresFact]
    public async Task The_server_reports_it_as_a_unique_violation()
    {
        var eventId = PostgresTestDatabase.Unique("evt");

        Context.Set<ProviderEventReceipt>().Add(Receipt(eventId));
        await _unitOfWork.SaveChangesAsync();
        Context.Set<ProviderEventReceipt>().Add(Receipt(eventId));

        var conflict = await Assert.ThrowsAsync<UniqueConstraintConflictException>(
            () => _unitOfWork.SaveChangesAsync());

        var postgres = Assert.IsType<PostgresException>(conflict.InnerException?.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
    }

    /// <summary>
    /// A different unique index reports a different name, and is NOT read as a replay.
    /// </summary>
    /// <remarks>
    /// The one that matters for safety. <c>payments</c> carries its own unique index on
    /// (provider, provider_reference), and if the handler's replay arm keyed on the SQLSTATE alone it
    /// would swallow a collision there too — answering the provider "recorded, do not send this
    /// again" about an event it had in fact done nothing with. The name is what separates them, and
    /// this proves the server supplies it and that the predicate says no.
    /// </remarks>
    [PostgresFact]
    public async Task A_different_unique_index_reports_a_different_name()
    {
        var reference = PostgresTestDatabase.Unique("ref");

        Context.Payments.Add(PaymentOn(reference));
        await _unitOfWork.SaveChangesAsync();

        Context.Payments.Add(PaymentOn(reference));

        var conflict = await Assert.ThrowsAsync<UniqueConstraintConflictException>(
            () => _unitOfWork.SaveChangesAsync());

        Assert.False(conflict.IsProviderEventReceipt);
        Assert.NotEqual(
            UniqueConstraintConflictException.ProviderEventReceiptConstraint,
            conflict.ConstraintName);
        // And it names the index that actually refused, which is what makes it diagnosable.
        Assert.Contains("payments", conflict.ConstraintName, StringComparison.Ordinal);
    }

    private static Payment PaymentOn(string reference)
    {
        var payment = Payment.Open(
            Id.New(),
            Id.New(),
            Money.Jod(75m),
            Provider,
            Build.Now.AddMinutes(30),
            Build.Now);
        payment.AttachProviderSession(reference, "https://example.invalid/checkout");
        payment.ClearDomainEvents();
        return payment;
    }
}
