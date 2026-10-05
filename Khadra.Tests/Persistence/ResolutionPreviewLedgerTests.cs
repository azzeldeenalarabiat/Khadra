using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Disputes;
using Khadra.Application.Disputes.Dtos;
using Khadra.Application.Disputes.ResolveDispute;
using Khadra.Application.Notifications;
using Khadra.Application.Payables.Pass;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Domain.Payables;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Khadra.Tests.Persistence;

/// <summary>
/// A dispute decision's preview against what the office payables ledger then records, through the stored path (Wave
/// 2 C1; E2E F37). The preview is taken and the decision made by the real handlers over the real repositories. The
/// ledger's own pass, reading the booking back from the database, then records the payable. Its outcome, lines and
/// figures must be the ones the administrator was shown.
/// </summary>
/// <remarks>
/// The default booking is 3 days at 30 JOD: an 18 deposit, a 6 commission. The ledger waits 10 minutes past finality.
/// </remarks>
public sealed class ResolutionPreviewLedgerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly PayablesHarness _harness;
    private readonly Id _adminId = Id.New();

    public ResolutionPreviewLedgerTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(options);
        context.Database.EnsureCreated();
        _harness = new PayablesHarness(options);
    }

    public void Dispose() => _connection.Dispose();

    private AdminDisputeHandlers Handlers(KhadraDbContext context, DateTimeOffset now)
    {
        var clock = new TestClock(now);
        var actor = Substitute.For<ICurrentActor>();
        actor.UserId.Returns(_adminId);
        actor.Role.Returns(UserRole.Admin);
        actor.Name.Returns("Rania Haddad");
        actor.CorrelationId.Returns("test");
        var dashboard = Substitute.For<IAdminDashboardSettings>();
        dashboard.SlaWarningThreshold.Returns(0.75m);
        var composer = new DisputeViewComposer(
            new BookingRepository(context),
            new BookingReader(context),
            new DisputeAdminReader(context),
            Substitute.For<IDocumentLinkSigner>(),
            new DisputeTicketRepository(context),
            dashboard,
            new OfficePayableRepository(context),
            clock,
            NullLogger<DisputeViewComposer>.Instance);
        return new AdminDisputeHandlers(
            new DisputeTicketRepository(context),
            new BookingRepository(context),
            new PaymentRepository(context),
            new DisputeAdminReader(context),
            composer,
            new DisputeAuditor(new AuditTrail(context), actor, clock),
            new DealerTeamNotifier(Substitute.For<INotifier>(), Substitute.For<IUserRepository>()),
            _harness.Settings,
            actor,
            clock,
            IssuanceHarness.UnitOfWork(context));
    }

    private async Task<Booking> ChangeBookingAsync(Id bookingId, Action<Booking> change)
    {
        Booking? changed = null;
        await _harness.Bookings.ChangeAsync(async context =>
        {
            changed = (await new BookingRepository(context).GetByIdAsync(bookingId))!;
            change(changed);
        });
        return changed!;
    }

    private async Task<DisputeTicket> OpenAsync(Booking booking, DateTimeOffset at)
    {
        var ticket = DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Something went wrong.", TimeSpan.FromHours(48), at).Value;
        ticket.ClearDomainEvents();
        await _harness.Bookings.ChangeAsync(context =>
        {
            context.DisputeTickets.Add(ticket);
            return Task.CompletedTask;
        });
        return ticket;
    }

    /// <summary>The preview on its own context, which must be left with nothing to save.</summary>
    private async Task<ResolutionPreviewDto> PreviewAsync(PreviewDisputeResolutionQuery query, DateTimeOffset now)
    {
        await using var context = _harness.NewContext();
        var result = await Handlers(context, now).Handle(query, CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.False(context.ChangeTracker.HasChanges());
        return result.Value;
    }

    private async Task ResolveAsync(ResolveDisputeCommand command, DateTimeOffset now)
    {
        await using var context = _harness.NewContext();
        var result = await Handlers(context, now).Handle(command, CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }

    private static void AssertRecordedAsPreviewed(ResolutionPreviewDto preview, OfficePayable payable)
    {
        Assert.Equal(preview.Outcome, payable.Outcome.Name);
        Assert.Equal(
            preview.Lines.Select(line => (line.Kind, line.Amount.Amount, line.TicketId)),
            payable.Lines.Select(line => (line.Kind.Name, line.Amount, line.SourceId?.Value)));
        Assert.Equal(preview.Office.Money.Amount, payable.OfficeMoney);
        Assert.Equal(preview.Office.Commission.Amount, payable.Commission);
        Assert.Equal(preview.Office.Charges.Amount, payable.OfficeCharges);
        Assert.Equal(preview.Office.Net.Amount, payable.Net);
        Assert.Equal(preview.CalculatorVersion, payable.CalculatorVersion);
        Assert.True(payable.RecordedAt >= preview.RecordedNotBefore);
    }

    [Fact]
    public async Task A_returned_bookings_decision_is_recorded_exactly_as_previewed()
    {
        _harness.Bookings.Now = _harness.Now;
        var (paid, _) = await _harness.Bookings.PaidAsync(PaymentProviders.Sandbox);
        var booking = await ChangeBookingAsync(paid.Id, stored =>
        {
            Assert.True(stored.RecordPickup(BookingParty.Dealer, Id.New(), stored.Period.Start).IsSuccess);
            Assert.True(stored.RecordReturn(BookingParty.Dealer, Id.New(), stored.Period.End).IsSuccess);
        });
        var ticket = await OpenAsync(booking, booking.Period.End.AddMinutes(30));
        var decidedAt = booking.Period.End.AddHours(1);

        // E3's shape: an explicit share for Khadra, and an office share the commission then takes whole.
        var preview = await PreviewAsync(new PreviewDisputeResolutionQuery(ticket.Id, 10m, 4m, 4m, null, null), decidedAt);
        Assert.Equal("RentalAfterDispute", preview.Outcome);
        Assert.Equal(0m, preview.Office.Net.Amount);

        await ResolveAsync(new ResolveDisputeCommand(ticket.Id, 10m, 4m, 4m, null, "Both partly at fault."), decidedAt);
        _harness.Now = preview.RecordedNotBefore;
        Assert.Contains(PayableStep.Recorded, await _harness.PassAsync());

        var payable = Assert.Single(await _harness.PayablesAsync());
        AssertRecordedAsPreviewed(preview, payable);
        Assert.Equal(decidedAt, payable.FinalAt);
    }

    [Fact]
    public async Task A_cancellations_decision_with_a_charge_is_recorded_as_previewed_once_its_window_closes()
    {
        _harness.Bookings.Now = _harness.Now;
        var (paid, _) = await _harness.Bookings.PaidAsync(PaymentProviders.Sandbox);
        var booking = await ChangeBookingAsync(paid.Id, stored =>
            Assert.True(stored.Cancel(BookingParty.Dealer, Id.New(), "No car.", stored.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess));
        Assert.Same(BookingParty.Dealer, booking.Penalty!.AttributedTo);
        var charge = booking.Penalty.MinAmount.Amount;
        var ticket = await OpenAsync(booking, booking.FinishedAt!.Value.AddMinutes(30));
        var decidedAt = booking.FinishedAt.Value.AddHours(1);

        var preview = await PreviewAsync(new PreviewDisputeResolutionQuery(ticket.Id, 18m, 0m, 0m, charge, null), decidedAt);
        Assert.Equal("DisputeDecided", preview.Outcome);
        Assert.Equal(booking.DisputeWindowEndsAt, preview.FurtherDecisionsPossibleUntil);

        await ResolveAsync(new ResolveDisputeCommand(ticket.Id, 18m, 0m, 0m, charge, "The office cancelled late."), decidedAt);
        // Not before its window closes, whatever was decided.
        _harness.Now = booking.DisputeWindowEndsAt!.Value.AddMinutes(-1);
        Assert.DoesNotContain(PayableStep.Recorded, await _harness.PassAsync());
        _harness.Now = preview.RecordedNotBefore;
        Assert.Contains(PayableStep.Recorded, await _harness.PassAsync());

        var payable = Assert.Single(await _harness.PayablesAsync());
        AssertRecordedAsPreviewed(preview, payable);
        Assert.Equal(booking.DisputeWindowEndsAt, payable.FinalAt);
    }
}
