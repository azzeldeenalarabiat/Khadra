using Khadra.Application.Bookings;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Disputes;
using Khadra.Application.Disputes.RaiseDispute;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Application.Disputes.ResolveDispute;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Disputes;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Disputes;

// Spec 3.3 end to end at the handler level: a party opens a ticket from a finished booking, the
// other side answers, and an Admin decides -- with the deposit split checked against the booking's
// own frozen figures, the booking closed in the same commit, and the decision written to the audit
// trail. The things held hardest: no money decision without a balancing split, no dealer charge
// outside the assessed range, and nothing an outsider can see or do.
public sealed class DisputeUseCaseTests
{
    private static readonly Id CustomerId = Id.New();
    private static readonly Id OwnerId = Id.New();
    private static readonly Id AdminId = Id.New();
    private static readonly Id StrangerId = Id.New();

    private sealed class Context
    {
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IDisputeTicketRepository Tickets { get; } = Substitute.For<IDisputeTicketRepository>();
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IBookingReader BookingReader { get; } = Substitute.For<IBookingReader>();
        public IDisputeAdminReader Names { get; } = Substitute.For<IDisputeAdminReader>();
        public IDocumentLinkSigner Signer { get; } = Substitute.For<IDocumentLinkSigner>();
        public IUploadTicketService Uploads { get; } = Substitute.For<IUploadTicketService>();
        public IDocumentStorage Storage { get; } = Substitute.For<IDocumentStorage>();
        public IAuditTrail AuditTrail { get; } = Substitute.For<IAuditTrail>();
        public ICurrentActor Actor { get; } = Substitute.For<ICurrentActor>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public TestClock Clock { get; } = new(Build.Now);
        public List<DisputeTicket> Added { get; } = [];
        public List<AuditEntry> Audited { get; } = [];

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Tickets.When(repository => repository.AddAsync(Arg.Any<DisputeTicket>(), Arg.Any<CancellationToken>()))
                .Do(call => Added.Add(call.Arg<DisputeTicket>()));
            AuditTrail.When(trail => trail.Record(Arg.Any<AuditEntry>()))
                .Do(call => Audited.Add(call.Arg<AuditEntry>()));
            BookingReader.ContextAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
                .Returns(new BookingContext(null, "Petra Wheels", "Layla Odeh", null, null));
            Names.NamesAsync(Arg.Any<IReadOnlyCollection<Id>>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<Guid, string>());
            Signer.Sign(Arg.Any<string>(), Arg.Any<DateTimeOffset>())
                .Returns(call => new SignedDocumentLink($"/api/v1/documents/{call.Arg<string>()}", Build.Now.AddMinutes(5)));
            // Every key has bytes behind it unless a test says otherwise.
            Storage.OpenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])));

            Actor.UserId.Returns(AdminId);
            Actor.Role.Returns(UserRole.Admin);
            Actor.Name.Returns("Rania Haddad");
            Actor.CorrelationId.Returns("test");
        }

        public Booking GivenBooking(Booking booking)
        {
            Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
            return booking;
        }

        public DisputeTicket GivenTicket(DisputeTicket ticket)
        {
            Tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
            return ticket;
        }

        public DisputeViewComposer Composer() => new(Bookings, BookingReader, Names, Signer, Clock);

        public RaiseDisputeHandlers Raise() => new(
            Bookings, Tickets, new BookingPartyResolver(Dealers), Composer(), Uploads, Storage,
            FakeDocumentPolicy.Default, TestBusinessRules.Provider(), Clock, UnitOfWork);

        public AdminDisputeHandlers Admin() => new(
            Tickets, Bookings, Names, Composer(), new DisputeAuditor(AuditTrail, Actor, Clock), Actor, Clock, UnitOfWork);
    }

    /// <summary>A booking the customer cancelled after paying: terminal, with the deposit held and a penalty assessed.</summary>
    private static Booking CancelledBooking(DateTimeOffset now)
    {
        var booking = Build.Booking(now: now, customerId: CustomerId, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        booking.Approve(Id.New(), now.AddMinutes(10));
        booking.ConfirmDepositPaid(Id.New(), now);
        // Past the free window, so a penalty is assessed against the customer.
        booking.Cancel(BookingParty.Customer, CustomerId, "Changed plans.", now.AddHours(3));
        booking.ClearDomainEvents();
        return booking;
    }

    private static DisputeTicket OpenTicket(Booking booking, DateTimeOffset now) =>
        DisputeTicket.Open(booking.Id, CustomerId, BookingParty.Customer, "The dealer never showed up.", TimeSpan.FromHours(48), now).Value;

    // ── Opening ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_customer_opens_a_ticket_on_their_finished_booking_with_the_configured_sla()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var key = $"disputes/{booking.Id.Value}/photo.jpg";

        var result = await context.Raise().Handle(
            new OpenDisputeCommand(CustomerId, booking.Id, "The dealer never showed up.", [key]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var ticket = Assert.Single(context.Added);
        Assert.Same(DisputeStatus.Open, ticket.Status);
        Assert.Same(BookingParty.Customer, ticket.OpenedByParty);
        // 48h is the configured Admin SLA (TestBusinessRules), not a constant in the handler.
        Assert.Equal(Build.Now.AddHours(48), ticket.SlaDeadline);
        Assert.Contains(key, ticket.Statements.Single().EvidenceStorageKeys);
        Assert.Equal("Petra Wheels", result.Value.Booking.DealerName);
        Assert.Single(result.Value.Statements.Single().Evidence);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_stranger_is_told_the_booking_does_not_exist()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));

        var result = await context.Raise().Handle(
            new OpenDisputeCommand(StrangerId, booking.Id, "Give me money.", []), CancellationToken.None);

        Assert.Equal("booking.not_found", result.Error.Code);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task A_booking_still_in_progress_cannot_be_disputed()
    {
        var context = new Context();
        var booking = context.GivenBooking(Build.ConfirmedBooking());
        var stillMine = Build.Booking(customerId: CustomerId);
        stillMine.Approve(Id.New(), Build.Now);
        stillMine.ConfirmDepositPaid(Id.New(), Build.Now);
        context.GivenBooking(stillMine);

        var result = await context.Raise().Handle(
            new OpenDisputeCommand(CustomerId, stillMine.Id, "Too early.", []), CancellationToken.None);

        Assert.Equal("dispute.booking_not_disputable", result.Error.Code);
        _ = booking;
    }

    [Fact]
    public async Task A_booking_whose_window_has_closed_cannot_be_disputed()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        // The window is the booking's own frozen seven days, measured from when it finished.
        context.Clock.UtcNow = Build.Now.AddDays(8);

        var result = await context.Raise().Handle(
            new OpenDisputeCommand(CustomerId, booking.Id, "Late.", []), CancellationToken.None);

        Assert.Equal("dispute.booking_not_disputable", result.Error.Code);
    }

    [Fact]
    public async Task Only_one_live_ticket_per_booking()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        context.Tickets.HasLiveTicketAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await context.Raise().Handle(
            new OpenDisputeCommand(CustomerId, booking.Id, "Again.", []), CancellationToken.None);

        Assert.Equal("dispute.already_open", result.Error.Code);
    }

    [Fact]
    public async Task Evidence_must_belong_to_this_booking_and_actually_exist()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var elsewhere = $"disputes/{Guid.NewGuid()}/photo.jpg";
        var missing = $"disputes/{booking.Id.Value}/never-uploaded.jpg";
        context.Storage.OpenAsync(missing, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Stream?>(null));

        var outside = await context.Raise().Handle(
            new OpenDisputeCommand(CustomerId, booking.Id, "Look.", [elsewhere]), CancellationToken.None);
        var unuploaded = await context.Raise().Handle(
            new OpenDisputeCommand(CustomerId, booking.Id, "Look.", [missing]), CancellationToken.None);

        Assert.Equal("dispute.evidence_outside_booking", outside.Error.Code);
        Assert.Equal("dispute.evidence_not_uploaded", unuploaded.Error.Code);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task An_evidence_upload_is_scoped_to_the_booking_and_refuses_odd_file_types()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        context.Uploads.Issue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
            .Returns(call => new UploadTicket("/api/v1/uploads/t", call.ArgAt<string>(0), Build.Now.AddMinutes(15)));

        var ok = await context.Raise().Handle(
            new RequestDisputeEvidenceUploadCommand(CustomerId, booking.Id, "dent.jpg", "image/jpeg"), CancellationToken.None);
        var exe = await context.Raise().Handle(
            new RequestDisputeEvidenceUploadCommand(CustomerId, booking.Id, "run.exe", "application/octet-stream"), CancellationToken.None);

        Assert.StartsWith($"disputes/{booking.Id.Value}/", ok.Value.StorageKey, StringComparison.Ordinal);
        Assert.EndsWith(".jpg", ok.Value.StorageKey, StringComparison.Ordinal);
        Assert.Equal("dispute.invalid_evidence_type", exe.Error.Code);
    }

    // ── Statements and withdrawal ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_dealer_answers_on_the_same_ticket_as_the_dealer_party()
    {
        var context = new Context();
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        var booking = Build.Booking(customerId: CustomerId, dealerId: dealer.Id, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        booking.Approve(Id.New(), Build.Now.AddMinutes(10));
        booking.ConfirmDepositPaid(Id.New(), Build.Now);
        booking.Cancel(BookingParty.Customer, CustomerId, "Changed plans.", Build.Now.AddHours(3));
        context.GivenBooking(booking);
        context.Dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        // The answer comes AFTER the opening statement; Statements is ordered by time.
        context.Clock.UtcNow = Build.Now.AddHours(6);

        var result = await context.Raise().Handle(
            new AddDisputeStatementCommand(OwnerId, ticket.Id, "We were there at 9 sharp.", []), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(2, ticket.Statements.Count);
        Assert.Same(BookingParty.Dealer, ticket.Statements.Last().Party);
    }

    [Fact]
    public async Task Only_the_opener_can_withdraw_and_a_withdrawn_ticket_charges_nobody()
    {
        var context = new Context();
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        var booking = Build.Booking(customerId: CustomerId, dealerId: dealer.Id, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        booking.Approve(Id.New(), Build.Now.AddMinutes(10));
        booking.ConfirmDepositPaid(Id.New(), Build.Now);
        booking.Cancel(BookingParty.Customer, CustomerId, "Changed plans.", Build.Now.AddHours(3));
        context.GivenBooking(booking);
        context.Dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));

        var byDealer = await context.Raise().Handle(new WithdrawDisputeCommand(OwnerId, ticket.Id), CancellationToken.None);
        var byOpener = await context.Raise().Handle(new WithdrawDisputeCommand(CustomerId, ticket.Id), CancellationToken.None);

        Assert.Equal("dispute.only_opener_can_withdraw", byDealer.Error.Code);
        Assert.True(byOpener.IsSuccess);
        Assert.Same(DisputeStatus.Withdrawn, ticket.Status);
        Assert.Null(ticket.Resolution);
    }

    [Fact]
    public async Task A_stranger_cannot_read_a_ticket()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));

        var result = await context.Raise().Handle(new GetMyDisputeQuery(StrangerId, ticket.Id), CancellationToken.None);

        Assert.Equal("dispute.not_found", result.Error.Code);
    }

    // ── The Admin's decision ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolving_splits_the_bookings_own_deposit_closes_the_booking_and_writes_the_audit_trail()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var held = booking.Pricing.DepositAmount.Amount;

        var result = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held / 2, held - (held / 2), 0m, null, "Split the difference; both sides partly at fault."),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(DisputeStatus.Resolved, ticket.Status);
        Assert.Equal(held, ticket.Resolution!.Deposit.DepositHeld.Amount);
        Assert.Equal(AdminId, ticket.Resolution.ResolvedByAdminId);
        // A cancelled booking is already terminal: closing it is a success no-op.
        Assert.Same(BookingStatus.Cancelled, booking.Status);

        var entry = Assert.Single(context.Audited);
        Assert.Same(AuditAction.DisputeResolved, entry.Action);
        Assert.Equal("Open", entry.PreviousValue);
        Assert.Contains($"of {held} JOD held", entry.NewValue, StringComparison.Ordinal);
        Assert.Equal("Split the difference; both sides partly at fault.", entry.Reason);
        Assert.Equal($"Dispute on {booking.Reference.Value}", entry.SubjectLabel);
        // One commit for the ticket, the booking and the audit entry together.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_split_that_does_not_add_up_to_the_deposit_is_refused_and_nothing_is_written()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));

        var result = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, 1m, 1m, 1m, null, "Wrong."), CancellationToken.None);

        Assert.Equal("dispute.disposition_unbalanced", result.Error.Code);
        Assert.Same(DisputeStatus.Open, ticket.Status);
        Assert.Empty(context.Audited);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_dealer_charge_is_refused_when_the_booking_blamed_the_customer()
    {
        var context = new Context();
        // Customer cancelled late: the assessed penalty is attributed to the CUSTOMER.
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var held = booking.Pricing.DepositAmount.Amount;

        var result = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held, 0m, 0m, 30m, "Charging the dealer anyway."), CancellationToken.None);

        Assert.Equal("dispute.dealer_charge_unassessed", result.Error.Code);
        Assert.Same(DisputeStatus.Open, ticket.Status);
    }

    [Fact]
    public async Task Resolving_a_returned_booking_completes_it_without_waiting_out_the_window()
    {
        var context = new Context();
        var booking = Build.ConfirmedBooking(terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        var start = booking.Period.Start;
        booking.RecordPickup(BookingParty.Dealer, Id.New(), start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), start.AddDays(3));
        context.GivenBooking(booking);
        var ticket = context.GivenTicket(
            DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Overcharged for fuel.", TimeSpan.FromHours(48), start.AddDays(3).AddHours(1)).Value);
        context.Clock.UtcNow = start.AddDays(3).AddHours(6);
        var held = booking.Pricing.DepositAmount.Amount;

        var result = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held, 0m, 0m, null, "Refund in full."), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.Completed, booking.Status);
        Assert.True(ticket.Resolution!.WaivesEverything);
    }

    [Fact]
    public async Task A_ticket_cannot_be_resolved_twice()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var held = booking.Pricing.DepositAmount.Amount;

        var first = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held, 0m, 0m, null, "Refund."), CancellationToken.None);
        var second = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, 0m, held, 0m, null, "Changed my mind."), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal("dispute.already_resolved", second.Error.Code);
        Assert.Single(context.Audited);
    }

    [Fact]
    public async Task Assigning_records_who_took_it_on_and_moves_it_under_review()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));

        var result = await context.Admin().Handle(new AssignDisputeCommand(ticket.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(DisputeStatus.UnderReview, ticket.Status);
        Assert.Equal(AdminId, ticket.AssignedAdminId);
        var entry = Assert.Single(context.Audited);
        Assert.Same(AuditAction.DisputeAssigned, entry.Action);
        Assert.Equal("Open", entry.PreviousValue);
        Assert.Equal("UnderReview", entry.NewValue);
    }

    [Fact]
    public async Task A_ticket_whose_booking_will_not_load_is_never_resolved()
    {
        var context = new Context();
        var ticket = context.GivenTicket(
            DisputeTicket.Open(Id.New(), CustomerId, BookingParty.Customer, "Orphan.", TimeSpan.FromHours(48), Build.Now).Value);

        var result = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, 0m, 0m, 0m, null, "Nothing held."), CancellationToken.None);

        Assert.Equal("dispute.booking_missing", result.Error.Code);
        Assert.Same(DisputeStatus.Open, ticket.Status);
    }

    /// <summary>
    /// The workspace must be told what a resolution has to add up to. It comes from the same helper
    /// the resolve handler validates against, so an admin can never be shown one basis and judged
    /// against another.
    /// </summary>
    [Fact]
    public async Task The_view_carries_the_deposit_a_resolution_must_split()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now));

        var result = await context.Admin().Handle(
            new GetDisputeForReviewQuery(ticket.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var held = BookingDisputeSettlement.DepositHeldFor(booking);
        Assert.Equal(held.Amount, result.Value.DepositHeld.Amount);
        Assert.Equal(held.CurrencyCode, result.Value.DepositHeld.Currency);
        // And the figure is the one a balanced split is actually checked against.
        var resolved = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, result.Value.DepositHeld.Amount, 0m, 0m, null, "Refunded in full."),
            CancellationToken.None);
        Assert.True(resolved.IsSuccess);
    }
}
