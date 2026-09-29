using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Payables.Dtos;
using Khadra.Application.Payables.Holds;
using Khadra.Application.Payables.Pass;
using Khadra.Application.Payables.ReadModels;
using Khadra.Application.Payables.Settlements;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Payables;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Khadra.Tests.Support;

/// <summary>
/// The office payables ledger with the real repositories, readers and unit of work over any database (payments Phase
/// 8): SQLite for the suite, a scratch PostgreSQL for the opt-in proofs. The settlement pass's payables step runs
/// exactly as <c>BookingSettlementService</c> runs it — one query for the work, then a context per booking and per
/// payable — and every administrator's action through the real audit trail.
/// </summary>
internal sealed class PayablesHarness(DbContextOptions<KhadraDbContext> options)
{
    /// <summary>Bookings, payments and parties, saved the way the documents' tests save them.</summary>
    public IssuanceHarness Bookings { get; } = new(options);

    /// <summary>The clock every handler reads.</summary>
    public DateTimeOffset Now { get; set; } = IssuanceHarness.Start.AddMinutes(1);

    public TestPayablesSettings Settings { get; } = new();

    /// <summary>Where the next pass's page of open payables starts: what <c>BookingSettlementService</c> keeps.</summary>
    public int VerifyOffset { get; set; }

    public Id AdminId { get; } = Id.New();

    public KhadraDbContext NewContext() => new(options);

    /// <summary>The ledger as the screens read it, on a fresh context.</summary>
    public async Task<T> ReadAsync<T>(Func<OfficeLedgerReader, Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        await using var context = NewContext();
        return await read(new OfficeLedgerReader(context));
    }

    /// <summary>One settlement pass's payables step, exactly as <c>BookingSettlementService</c> runs it.</summary>
    public async Task<List<PayableStep>> PassAsync()
    {
        PayableWork work;
        await using (var context = NewContext())
        {
            // Today's dispute window is 48 hours, as every test booking's frozen one is.
            work = await new ListPayableWorkHandler(new PayableWorkReader(context), Settings, TestBusinessRules.Provider(), new TestClock(Now))
                .Handle(new ListPayableWorkQuery(VerifyOffset), CancellationToken.None);
        }

        var steps = new List<PayableStep>();
        foreach (var bookingId in work.BookingsToRecord)
        {
            await using var context = NewContext();
            steps.Add(await RecordHandler(context).Handle(new RecordOfficePayableCommand(bookingId), CancellationToken.None));
        }

        foreach (var payableId in work.PayablesToVerify)
        {
            await using var context = NewContext();
            steps.Add(await VerifyHandler(context).Handle(new VerifyOfficePayableCommand(payableId), CancellationToken.None));
        }

        VerifyOffset = work.NextVerifyOffset;
        return steps;
    }

    public RecordOfficePayableHandler RecordHandler(KhadraDbContext context) =>
        new(
            Facts(context),
            new OfficePayableRepository(context),
            new OfficePayableHoldRepository(context),
            Settings,
            IssuanceHarness.UnitOfWork(context),
            new TestClock(Now),
            new RecordingLogger<RecordOfficePayableHandler>());

    public VerifyOfficePayableHandler VerifyHandler(KhadraDbContext context) =>
        new(
            new OfficePayableRepository(context),
            new OfficePayableHoldRepository(context),
            Facts(context),
            Settings,
            IssuanceHarness.UnitOfWork(context),
            new TestClock(Now),
            new RecordingLogger<VerifyOfficePayableHandler>());

    public async Task<CSharpFunctionalExtensions.Result<OfficeSettlementDetailDto, Error>> SettleAsync(
        Id dealerId,
        string provider,
        decimal expected,
        DateOnly? paidOn = null,
        string currency = "JOD",
        string? reference = "TRX-1",
        string? note = null)
    {
        await using var context = NewContext();
        return await SettleHandler(context).Handle(
            new RecordOfficeSettlementCommand(dealerId, currency, provider, expected, paidOn ?? Build.AmmanDate(Now), reference, note, AdminId),
            CancellationToken.None);
    }

    public RecordOfficeSettlementHandler SettleHandler(KhadraDbContext context) =>
        new(
            new OfficeLedgerReader(context),
            new OfficePayableRepository(context),
            new OfficePayableHoldRepository(context),
            new OfficeSettlementRepository(context),
            Facts(context),
            new FinancialDocumentSeriesCounter(context),
            Audit(context),
            IssuanceHarness.UnitOfWork(context),
            DocumentFixtures.Amman,
            new TestClock(Now),
            new RecordingLogger<RecordOfficeSettlementHandler>());

    public async Task<CSharpFunctionalExtensions.Result<OfficeSettlementDetailDto, Error>> VoidAsync(Id settlementId, string? reason)
    {
        await using var context = NewContext();
        return await new VoidOfficeSettlementHandler(
                new OfficeSettlementRepository(context),
                new OfficePayableRepository(context),
                new OfficeLedgerReader(context),
                Audit(context),
                IssuanceHarness.UnitOfWork(context),
                new TestClock(Now),
                new RecordingLogger<VoidOfficeSettlementHandler>())
            .Handle(new VoidOfficeSettlementCommand(settlementId, AdminId, reason), CancellationToken.None);
    }

    public async Task<CSharpFunctionalExtensions.Result<OfficePayableDto, Error>> HoldAsync(Id payableId, string? reason)
    {
        await using var context = NewContext();
        return await HoldHandlers(context).Handle(new HoldOfficePayableCommand(payableId, AdminId, reason), CancellationToken.None);
    }

    public async Task<CSharpFunctionalExtensions.Result<OfficePayableDto, Error>> ReleaseAsync(Id payableId, string? note = null)
    {
        await using var context = NewContext();
        return await HoldHandlers(context).Handle(new ReleaseOfficePayableCommand(payableId, AdminId, note), CancellationToken.None);
    }

    private OfficePayableHoldHandlers HoldHandlers(KhadraDbContext context) =>
        new(
            new OfficePayableRepository(context),
            new OfficePayableHoldRepository(context),
            new OfficeLedgerReader(context),
            Audit(context),
            IssuanceHarness.UnitOfWork(context),
            new TestClock(Now));

    public async Task<List<OfficePayable>> PayablesAsync()
    {
        await using var context = NewContext();
        return await context.OfficePayables.AsNoTracking().Include(payable => payable.Lines).ToListAsync();
    }

    public async Task<List<OfficePayableHold>> HoldsAsync()
    {
        await using var context = NewContext();
        return await context.OfficePayableHolds.AsNoTracking().ToListAsync();
    }

    public async Task<List<AuditEntry>> AuditAsync()
    {
        await using var context = NewContext();
        return await context.AuditEntries.AsNoTracking().ToListAsync();
    }

    private static FinancialDocumentFactsReader Facts(KhadraDbContext context) =>
        new(new BookingRepository(context), new PaymentRepository(context), new DisputeTicketRepository(context), context);

    private AdminActionRecorder Audit(KhadraDbContext context)
    {
        var actor = Substitute.For<ICurrentActor>();
        actor.UserId.Returns(AdminId);
        actor.Role.Returns(UserRole.Admin);
        actor.Name.Returns("Test Admin");
        actor.CorrelationId.Returns("test");
        return new AdminActionRecorder(new AuditTrail(context), actor, new TestClock(Now));
    }
}

/// <summary>The payables pass's settings a test controls.</summary>
internal sealed class TestPayablesSettings : IPayablesSettings
{
    public TimeSpan FinalityMargin { get; set; } = TimeSpan.FromMinutes(10);

    public int MaxBookingsPerPass { get; set; } = 100;

    public int MaxVerificationsPerPass { get; set; } = 50;

    public TimeSpan RetryInitialDelay { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromHours(6);
}
