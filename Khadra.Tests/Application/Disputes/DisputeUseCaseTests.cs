using Khadra.Application.Bookings;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Common.Ports;
using Khadra.Application.Disputes;
using Khadra.Application.Disputes.Dtos;
using Khadra.Application.Disputes.RaiseDispute;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Application.Disputes.ResolveDispute;
using Khadra.Application.Notifications;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Disputes;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Domain.Payables.Repositories;
using System.Text.Json;
using Khadra.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
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

        /// <summary>The office payables ledger: nothing recorded unless a test says otherwise.</summary>
        public IOfficePayableRepository Payables { get; } = Substitute.For<IOfficePayableRepository>();

        public TestPayablesSettings PayablesSettings { get; } = new();

        /// <summary>The work queue's at-risk threshold, as configured by default (AdminDashboardOptions).</summary>
        public IAdminDashboardSettings Dashboard { get; } = DashboardWith(0.75m);

        private static IAdminDashboardSettings DashboardWith(decimal threshold)
        {
            var settings = Substitute.For<IAdminDashboardSettings>();
            settings.SlaWarningThreshold.Returns(threshold);
            return settings;
        }
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
                .Returns(new BookingContext(null, "Petra Wheels", false, null, "Layla Odeh", false, null, null));
            Names.NamesAsync(Arg.Any<IReadOnlyCollection<Id>>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<Guid, string>());
            Signer.Sign(Arg.Any<string>(), Arg.Any<Id>(), Arg.Any<DateTimeOffset>())
                .Returns(call => new SignedDocumentLink($"/api/v1/documents/{call.Arg<string>()}", Build.Now.AddMinutes(5)));
            // Every key has bytes behind it unless a test says otherwise.
            Storage.OpenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])));

            Actor.UserId.Returns(AdminId);
            Actor.Role.Returns(UserRole.Admin);
            Actor.Name.Returns("Rania Haddad");
            Actor.CorrelationId.Returns("test");
            // Registered once: both handlers stage through the same notifier.
            Notifier.When(n => n.Raise(Arg.Any<Notification>())).Do(call => Told.Add(call.Arg<Notification>()));
            Notifier.When(n => n.RaiseMany(Arg.Any<IEnumerable<Notification>>()))
                .Do(call => Told.AddRange(call.Arg<IEnumerable<Notification>>()));
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

        public DisputeViewComposer Composer() =>
            new(Bookings, BookingReader, Names, Signer, Actor, Tickets, Dashboard, Payables, Clock, NullLogger<DisputeViewComposer>.Instance);

        /// <summary>The booking's resolved tickets as the repository answers them, oldest first (item 169).</summary>
        public void GivenResolved(Booking booking, params DisputeTicket[] resolved) =>
            Tickets.ListResolvedForBookingAsync(booking.Id, Arg.Any<CancellationToken>())
                .Returns(resolved.OrderBy(ticket => ticket.OpenedAt).ToList());

        public RaiseDisputeHandlers Raise() => new(
            Bookings, Tickets, new BookingPartyResolver(Dealers), Composer(), Uploads, Storage,
            FakeDocumentPolicy.Default, TestBusinessRules.Provider(), Dealers, Team(), Clock, UnitOfWork);

        public IPaymentRepository Payments { get; } = Substitute.For<IPaymentRepository>();

        public INotifier Notifier { get; } = Substitute.For<INotifier>();

        /// <summary>Every notification either handler staged.</summary>
        public List<Notification> Told { get; } = [];

        private DealerTeamNotifier Team() => new(Notifier, Substitute.For<IUserRepository>());

        /// <summary>The booking's office, answering for its id and for its owner: the owner and one employee.</summary>
        public Dealer Office(Id employeeUserId)
        {
            var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
            dealer.HireEmployee(employeeUserId, canViewReports: false, Build.Now);
            Dealers.GetByIdAsync(dealer.Id, Arg.Any<CancellationToken>()).Returns(dealer);
            Dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);
            return dealer;
        }

        public AdminDisputeHandlers Admin() => new(
            Tickets, Bookings, Payments, Names, Composer(), new DisputeAuditor(AuditTrail, Actor, Clock),
            Team(), Dealers, PayablesSettings, Actor, Clock, UnitOfWork);
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

    /// <summary>
    /// An evidence link opens in the session of the person the view was composed for and nobody else's (pre-launch
    /// item 14): the customer who just opened the ticket here.
    /// </summary>
    [Fact]
    public async Task Evidence_links_are_minted_for_the_signed_in_reader()
    {
        var context = new Context();
        context.Actor.UserId.Returns(CustomerId);
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var key = $"disputes/{booking.Id.Value}/photo.jpg";

        await context.Raise().Handle(new OpenDisputeCommand(CustomerId, booking.Id, "The dealer never showed up.", [key]), CancellationToken.None);

        context.Signer.Received(1).Sign(key, CustomerId, Build.Now);
        context.Signer.DidNotReceive().Sign(Arg.Any<string>(), Arg.Is<Id>(viewer => viewer != CustomerId), Arg.Any<DateTimeOffset>());
    }

    // ── Who hears that a dispute was opened (Fix & Polish Wave 3: C5, D10) ──────────────────────

    /// <summary>A customer cancelled after paying, on this office's car: terminal, and disputable for seven days.</summary>
    private static Booking CancelledAt(Dealer office)
    {
        var booking = Build.Booking(customerId: CustomerId, dealerId: office.Id, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        booking.Approve(Id.New(), Build.Now.AddMinutes(10));
        booking.ConfirmDepositPaid(Id.New(), Build.Now);
        booking.Cancel(BookingParty.Customer, CustomerId, "Changed plans.", Build.Now.AddHours(3));
        booking.ClearDomainEvents();
        return booking;
    }

    /// <summary>
    /// The customer's own dispute: the whole office is told, naming no customer, and the customer has a confirmation
    /// whose subject is the BOOKING (installed apps open the booking) and whose moment is the ticket's frozen SLA.
    /// </summary>
    [Fact]
    public async Task A_customer_opening_a_dispute_tells_the_whole_office_and_confirms_it_to_the_customer()
    {
        var context = new Context();
        var employee = Id.New();
        var office = context.Office(employee);
        var booking = context.GivenBooking(CancelledAt(office));
        context.Clock.UtcNow = Build.Now.AddHours(4);

        var result = await context.Raise().Handle(
            new OpenDisputeCommand(CustomerId, booking.Id, "The office never showed up.", []), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var ticket = Assert.Single(context.Added);
        var team = context.Told.Where(told => told.Kind == NotificationKind.DisputeOpened).ToList();
        Assert.Equal(
            new[] { OwnerId, employee }.OrderBy(id => id.Value),
            team.Select(told => told.RecipientUserId).OrderBy(id => id.Value));
        Assert.All(team, told =>
        {
            Assert.Equal(ticket.Id, told.SubjectId);
            Assert.Null(told.ActorUserId);
            Assert.False(told.IsFromPlatform);
        });

        var confirmation = Assert.Single(context.Told, told => told.Kind == NotificationKind.YourDisputeOpened);
        Assert.Equal(CustomerId, confirmation.RecipientUserId);
        Assert.Equal(booking.Id, confirmation.SubjectId);
        Assert.Equal(booking.Reference.Value, confirmation.SubjectReference);
        Assert.Equal(ticket.SlaDeadline, confirmation.DueAt);
        Assert.True(confirmation.IsFromPlatform);
        Assert.Equal([NotificationChannel.Push, NotificationChannel.Email], NotificationKind.YourDisputeOpened.DeliveredOn());
    }

    /// <summary>
    /// A dispute the office opened: its colleagues are told, the opener is not, and the customer hears through the
    /// kind every installed app already opens at the dispute (C10).
    /// </summary>
    [Fact]
    public async Task An_office_opening_a_dispute_tells_its_colleagues_and_updates_the_customer()
    {
        var context = new Context();
        var employee = Id.New();
        var office = context.Office(employee);
        var booking = context.GivenBooking(CancelledAt(office));
        context.Clock.UtcNow = Build.Now.AddHours(4);

        var result = await context.Raise().Handle(
            new OpenDisputeCommand(OwnerId, booking.Id, "The customer left the car damaged.", []), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var ticket = Assert.Single(context.Added);
        var colleague = Assert.Single(context.Told, told => told.Kind == NotificationKind.DisputeOpened);
        Assert.Equal(employee, colleague.RecipientUserId);
        Assert.Equal(OwnerId, colleague.ActorUserId);
        var update = Assert.Single(context.Told, told => told.RecipientUserId == CustomerId);
        Assert.Same(NotificationKind.YourDisputeUpdated, update.Kind);
        Assert.Equal(ticket.Id, update.SubjectId);
        Assert.DoesNotContain(context.Told, told => told.Kind == NotificationKind.YourDisputeOpened);
    }

    [Fact]
    public async Task A_refused_opening_tells_nobody()
    {
        var context = new Context();
        var office = context.Office(Id.New());
        var booking = context.GivenBooking(CancelledAt(office));
        context.Tickets.HasLiveTicketAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(true);

        await context.Raise().Handle(new OpenDisputeCommand(CustomerId, booking.Id, "Again.", []), CancellationToken.None);

        Assert.Empty(context.Told);
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
        // The decision's parts, not an English sentence (pre-launch item 50): the console words them in each language.
        var parts = Parts(entry.NewValue);
        Assert.Equal("JOD", parts["currency"]);
        Assert.Equal(Money.AtScale(held).ToString(System.Globalization.CultureInfo.InvariantCulture), parts["held"]);
        Assert.Equal(Money.AtScale(held / 2).ToString(System.Globalization.CultureInfo.InvariantCulture), parts["refund"]);
        Assert.False(parts.ContainsKey("charge"));
        Assert.DoesNotContain("Resolved", entry.NewValue, StringComparison.Ordinal);
        Assert.Equal("Split the difference; both sides partly at fault.", entry.Reason);
        // The bare reference, never a sentence: the table can never be rewritten, so English written
        // into it stays English on every screen that reads it, in every language, for ever.
        Assert.Equal(booking.Reference.Value, entry.SubjectLabel);
        Assert.Same(AuditEntityType.Dispute, entry.EntityType);
        Assert.Equal(ticket.Id, entry.EntityId);
        // One commit for the ticket, the booking and the audit entry together.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Wave 2: the currency's precision, the audit line's scale, the administrator's own fields ─────

    /// <summary>
    /// Money rounds silently, so a fourth decimal used to be rounded away and the split decided in a form the
    /// administrator never typed (E2E F36). Refused, with a code the console words, before anything changes.
    /// </summary>
    [Theory]
    [InlineData("refund")]
    [InlineData("platform")]
    [InlineData("charge")]
    public async Task An_amount_with_more_places_than_the_currency_has_is_refused_and_nothing_is_decided(string leg)
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var held = booking.Pricing.DepositAmount.Amount;
        var command = leg switch
        {
            "refund" => new ResolveDisputeCommand(ticket.Id, held - 0.0005m, 0.0005m, 0m, null, "A fourth decimal."),
            "platform" => new ResolveDisputeCommand(ticket.Id, held - 1.2345m, 1.2345m, 0m, null, "A fourth decimal."),
            _ => new ResolveDisputeCommand(ticket.Id, held, 0m, 0m, 0.0001m, "A fourth decimal."),
        };

        var result = await context.Admin().Handle(command, CancellationToken.None);

        Assert.Equal("dispute.amount_precision", result.Error.Code);
        Assert.Same(DisputeStatus.Open, ticket.Status);
        Assert.Empty(context.Audited);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The audit line is stored for ever: every figure at full scale and in the invariant culture, so a server
    /// running with a decimal comma cannot write "1,5" into it (E2E F36).
    /// </summary>
    [Fact]
    public async Task The_audit_line_is_written_at_full_scale_whatever_the_servers_culture()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var held = booking.Pricing.DepositAmount.Amount;
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var result = await context.Admin().Handle(
                new ResolveDisputeCommand(ticket.Id, held - 1.5m, 1.5m, 0m, null, "Most of it back, a little kept."),
                CancellationToken.None);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }

        var parts = Parts(Assert.Single(context.Audited).NewValue);
        Assert.Equal("1.500", parts["platform"]);
        Assert.Equal(Money.AtScale(held - 1.5m).ToString(System.Globalization.CultureInfo.InvariantCulture), parts["refund"]);
        Assert.Equal("0.000", parts["dealer"]);
    }

    /// <summary>A charge on the office beyond the deposit travels as its own figure, in its own currency.</summary>
    [Fact]
    public async Task A_charge_on_the_office_is_one_more_part_of_the_audit_value()
    {
        var context = new Context();
        var (booking, _) = Build.PaidBooking(customerId: CustomerId, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        booking.ClearDomainEvents();
        context.GivenBooking(booking);
        var min = booking.Penalty!.MinAmount.Amount;
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));

        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, 18m, 0m, 0m, min, "The office cancelled late."), CancellationToken.None)).IsSuccess);

        var parts = Parts(Assert.Single(context.Audited).NewValue);
        Assert.Equal(Money.AtScale(min).ToString(System.Globalization.CultureInfo.InvariantCulture), parts["charge"]);
        Assert.Equal("JOD", parts["chargeCurrency"]);
        Assert.Equal("18.000", parts["refund"]);
    }

    private static Dictionary<string, string> Parts(string? value)
    {
        Assert.NotNull(value);
        using var document = System.Text.Json.JsonDocument.Parse(value);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.ToString());
    }

    /// <summary>The SLA panel is coloured by the work queue's own rule, at the same threshold (E2E F42).</summary>
    [Theory]
    [InlineData(1, "OnTime")]
    [InlineData(36, "AtRisk")]
    [InlineData(48, "Overdue")]
    public void A_live_tickets_sla_state_follows_the_work_queues_rule(int hoursSinceOpening, string expected)
    {
        var ticket = OpenTicket(CancelledBooking(Build.Now), Build.Now);

        Assert.Equal(expected, DisputeSlaStates.For(ticket, 0.75m, Build.Now.AddHours(hoursSinceOpening)));
    }

    [Fact]
    public void A_ticket_no_longer_live_is_closed_whatever_the_clock_says()
    {
        var ticket = OpenTicket(CancelledBooking(Build.Now), Build.Now);
        Assert.True(ticket.Withdraw(CustomerId, Build.Now.AddHours(1)).IsSuccess);

        Assert.Equal(DisputeSlaStates.Closed, DisputeSlaStates.For(ticket, 0.75m, Build.Now.AddDays(10)));
    }

    /// <summary>
    /// What the office was charged earlier and the SLA panel are the administrator's alone: a customer is never
    /// shown the office's charges (owner decision 3), and the installed app receives this DTO.
    /// </summary>
    [Fact]
    public async Task Only_the_administrators_copy_carries_the_earlier_charge_and_the_sla_state()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var composer = context.Composer();

        var admin = await composer.ComposeAsync(ticket, booking, BookingParty.Admin, CancellationToken.None);
        var customer = await composer.ComposeAsync(ticket, booking, BookingParty.Customer, CancellationToken.None);
        var office = await composer.ComposeAsync(ticket, booking, BookingParty.Dealer, CancellationToken.None);

        Assert.Equal("0.000", admin.ChargedToDealerEarlier!.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(DisputeSlaStates.OnTime, admin.SlaState);
        Assert.Null(customer.ChargedToDealerEarlier);
        Assert.Null(customer.SlaState);
        Assert.Null(office.ChargedToDealerEarlier);
        Assert.Null(office.SlaState);
    }

    /// <summary>
    /// A paid booking the customer cancelled inside the free window has already sent its deposit back
    /// (owner, 2026-09-24). It is still disputable, but a resolution may move none of that money:
    /// any refund is refused, and the all-zero resolution records no refund at all.
    /// </summary>
    [Fact]
    public async Task A_free_cancelled_paid_booking_leaves_a_dispute_nothing_to_split()
    {
        var context = new Context();
        var booking = Build.Booking(now: Build.Now, customerId: CustomerId, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        booking.Approve(Id.New(), Build.Now.AddMinutes(10));
        booking.ConfirmDepositPaid(Id.New(), Build.Now.AddMinutes(20));
        Assert.True(booking.Cancel(BookingParty.Customer, CustomerId, "Plans changed.", Build.Now.AddMinutes(30)).IsSuccess);
        Assert.True(booking.ReturnsDepositOnCancellation);
        booking.ClearDomainEvents();
        context.GivenBooking(booking);
        var held = booking.Pricing.DepositAmount.Amount;

        var refused = await context.Admin().Handle(
            new ResolveDisputeCommand(context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(1))).Id, held, 0m, 0m, null, "Refund in full."),
            CancellationToken.None);
        Assert.Equal("dispute.disposition_unbalanced", refused.Error.Code);

        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(1)));
        var resolved = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, 0m, 0m, 0m, null, "The deposit already went back with the free cancellation."),
            CancellationToken.None);

        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error.Code : null);
        Assert.Equal(0m, ticket.Resolution!.Deposit.DepositHeld.Amount);
        await context.Payments.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    /// <summary>
    /// Phase 3: a ticket opened in the very instant the dispute window closed can meet a deposit the
    /// sweep already released to the customer. It holds nothing to split — only the all-zero
    /// resolution is accepted, and it records no second refund of money the customer already has.
    /// </summary>
    [Fact]
    public async Task A_deposit_already_released_leaves_a_late_ticket_nothing_to_split()
    {
        var context = new Context();
        var (booking, payment) = Build.PaidBooking(customerId: CustomerId);
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", Build.Now.AddHours(3)).IsSuccess);
        booking.ClearDomainEvents();
        context.GivenBooking(booking);
        context.Payments.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);
        context.Payments.GetAppliedForBookingAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(payment);
        Assert.NotNull(payment.RefundHeldDeposit(Money.Jod(18m), Build.Now.AddDays(3)).Value);
        var held = booking.Pricing.DepositAmount.Amount;

        var refused = await context.Admin().Handle(
            new ResolveDisputeCommand(context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4))).Id, held, 0m, 0m, null, "Refund in full."),
            CancellationToken.None);
        Assert.Equal("dispute.disposition_unbalanced", refused.Error.Code);

        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var resolved = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, 0m, 0m, 0m, null, "The deposit already went back when the window closed."),
            CancellationToken.None);

        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error.Code : null);
        Assert.Equal(0m, ticket.Resolution!.Deposit.DepositHeld.Amount);
        Assert.DoesNotContain(payment.Refunds, refund => refund.Reason == RefundReason.DisputeResolution);
    }

    // ── A later dispute splits only what earlier ones left (owner, 2026-09-26; item 169) ─────────

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static async Task<DisputeTicket> ResolvedFirst(Context context, Booking booking, decimal refund, decimal platform, decimal dealer)
    {
        var first = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var resolved = await context.Admin().Handle(
            new ResolveDisputeCommand(first.Id, refund, platform, dealer, null, "First decision."),
            CancellationToken.None);
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error.Code : null);
        context.GivenResolved(booking, first);
        return first;
    }

    /// <summary>
    /// The owner's first example: the first dispute decides the whole 18.000, so a second one may still
    /// OPEN (the installed app sends that request) but has 0.000 to split, and only an all-zero decision
    /// is accepted. The server says so in the view; no client subtracts.
    /// </summary>
    [Theory]
    [InlineData(18, 0, 0)]  // "Refund the customer"
    [InlineData(9, 0, 9)]   // an even split
    [InlineData(13, 0, 5)]  // the "Partial" option: 5 to the office, the rest back
    public async Task A_second_dispute_after_a_whole_split_can_open_but_splits_nothing(int refund, int platform, int dealer)
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        Assert.Equal(18m, booking.Pricing.DepositAmount.Amount);
        await ResolvedFirst(context, booking, refund, platform, dealer);
        context.Clock.UtcNow = Build.Now.AddHours(6);

        var opened = await context.Raise().Handle(
            new OpenDisputeCommand(CustomerId, booking.Id, "Something else went wrong.", []),
            CancellationToken.None);
        Assert.True(opened.IsSuccess, opened.IsFailure ? opened.Error.Code : null);
        var second = context.GivenTicket(Assert.Single(context.Added));
        Assert.Equal(0m, opened.Value.DepositHeld.Amount);
        Assert.Equal(18m, opened.Value.DepositOnBooking!.Amount);
        Assert.Equal(18m, opened.Value.DecidedByEarlierTickets!.Amount);

        var refused = await context.Admin().Handle(
            new ResolveDisputeCommand(second.Id, 1m, 0m, 0m, null, "Any more money."),
            CancellationToken.None);
        Assert.Equal("dispute.disposition_unbalanced", refused.Error.Code);

        var closed = await context.Admin().Handle(
            new ResolveDisputeCommand(second.Id, 0m, 0m, 0m, null, "The deposit was already decided by the first dispute."),
            CancellationToken.None);
        Assert.True(closed.IsSuccess, closed.IsFailure ? closed.Error.Code : null);
        Assert.Equal(0m, second.Resolution!.Deposit.DepositHeld.Amount);
    }

    /// <summary>
    /// The owner's second example: an earlier decision allocated only 5.000 of the 18.000, so the next
    /// dispute operates on the remaining 13.000 — every fils of it, and never the original deposit.
    /// Today's resolve always decides the whole of its basis, so the partial decision is recorded
    /// directly: this pins what the handler does with one, however it came to exist.
    /// </summary>
    [Fact]
    public async Task A_second_dispute_after_a_partial_decision_splits_only_the_remaining_deposit()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var first = OpenTicket(booking, Build.Now.AddHours(4));
        Assert.True(first.Resolve(DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(5m), Money.Jod(0m), Money.Jod(0m), Money.Jod(5m)).Value,
            null, null, "Five to the office for the damage.", AdminId, Build.Now.AddHours(5)).Value).IsSuccess);
        context.GivenResolved(booking, first);
        var second = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(6)));

        var view = (await context.Admin().Handle(new GetDisputeForReviewQuery(second.Id), CancellationToken.None)).Value;
        Assert.Equal(13m, view.DepositHeld.Amount);
        Assert.Equal(18m, view.DepositOnBooking!.Amount);
        Assert.Equal(5m, view.DecidedByEarlierTickets!.Amount);

        foreach (var (refund, platform, dealer) in new[] { (14m, 0m, 0m), (18m, 0m, 0m), (8m, 0m, 0m) })
        {
            var refused = await context.Admin().Handle(
                new ResolveDisputeCommand(second.Id, refund, platform, dealer, null, "Not the remainder."), CancellationToken.None);
            Assert.Equal("dispute.disposition_unbalanced", refused.Error.Code);
        }

        var resolved = await context.Admin().Handle(
            new ResolveDisputeCommand(second.Id, 10m, 3m, 0m, null, "The rest, decided."), CancellationToken.None);
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error.Code : null);
        Assert.Equal(13m, second.Resolution!.Deposit.DepositHeld.Amount);
        // Both decisions together: exactly the deposit, never more.
        Assert.Equal(18m, first.Resolution!.Deposit.DepositHeld.Amount + second.Resolution.Deposit.DepositHeld.Amount);
    }

    /// <summary>
    /// A second, all-zero resolution on a booking with a REAL applied payment records no second refund:
    /// the zero customer leg returns before the payment is touched, which is all that stands between
    /// the handler and <c>Payment.RequestRefund</c> refusing a zero amount.
    /// </summary>
    [Fact]
    public async Task A_second_all_zero_resolution_records_no_second_refund_on_the_payment()
    {
        var context = new Context();
        var (booking, payment) = Build.PaidBooking(customerId: CustomerId, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        // Past the free window: the deposit stays held and a penalty is assessed against the customer.
        Assert.True(booking.Cancel(BookingParty.Customer, CustomerId, "Changed plans.", Build.Now.AddHours(3)).IsSuccess);
        booking.ClearDomainEvents();
        context.GivenBooking(booking);
        context.Payments.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);
        context.Payments.GetAppliedForBookingAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(payment);

        var first = await ResolvedFirst(context, booking, 9, 0, 9);
        var second = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(8)));
        var closed = await context.Admin().Handle(
            new ResolveDisputeCommand(second.Id, 0m, 0m, 0m, null, "The deposit was already decided by the first dispute."),
            CancellationToken.None);

        Assert.True(closed.IsSuccess, closed.IsFailure ? closed.Error.Code : null);
        var refund = Assert.Single(payment.Refunds);
        Assert.Same(RefundReason.DisputeResolution, refund.Reason);
        Assert.Equal(9m, refund.Amount.Amount);
        Assert.Equal(first.Id, refund.DisputeTicketId);
    }

    /// <summary>A withdrawn ticket decided nothing: the next one may split the whole deposit.</summary>
    [Fact]
    public async Task A_withdrawn_first_dispute_leaves_the_whole_deposit_to_the_next()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var withdrawn = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        Assert.True(withdrawn.Withdraw(CustomerId, Build.Now.AddHours(5)).IsSuccess);
        context.GivenResolved(booking);

        var second = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(6)));
        var view = await context.Admin().Handle(new GetDisputeForReviewQuery(second.Id), CancellationToken.None);
        Assert.Equal(18m, view.Value.DepositHeld.Amount);
        Assert.Equal(0m, view.Value.DecidedByEarlierTickets!.Amount);

        var resolved = await context.Admin().Handle(
            new ResolveDisputeCommand(second.Id, 18m, 0m, 0m, null, "Refund in full."),
            CancellationToken.None);
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error.Code : null);
    }

    /// <summary>
    /// A resolved ticket keeps the basis it was decided against, whatever later tickets do: "earlier"
    /// means opened before it, never "any other".
    /// </summary>
    [Fact]
    public async Task A_resolved_dispute_keeps_its_own_basis_after_a_later_one_resolves()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var first = await ResolvedFirst(context, booking, 9, 0, 9);
        var second = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(8)));
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(second.Id, 0m, 0m, 0m, null, "Nothing left."), CancellationToken.None)).IsSuccess);
        context.GivenResolved(booking, first, second);

        var firstView = (await context.Admin().Handle(new GetDisputeForReviewQuery(first.Id), CancellationToken.None)).Value;
        var secondView = (await context.Admin().Handle(new GetDisputeForReviewQuery(second.Id), CancellationToken.None)).Value;

        Assert.Equal(18m, firstView.DepositHeld.Amount);
        Assert.Equal(0m, firstView.DecidedByEarlierTickets!.Amount);
        Assert.Equal(0m, secondView.DepositHeld.Amount);
        Assert.Equal(18m, secondView.DecidedByEarlierTickets!.Amount);
        // Two decisions, one deposit: never more allocated than it held.
        var allocated = new[] { first, second }.Sum(ticket =>
            ticket.Resolution!.Deposit.RefundToCustomer.Amount +
            ticket.Resolution.Deposit.RetainedByPlatform.Amount +
            ticket.Resolution.Deposit.TransferredToDealer.Amount);
        Assert.Equal(18m, allocated);
    }

    /// <summary>Earlier decisions that add up to more than the deposit are a data fault: refused, never floored at zero.</summary>
    [Fact]
    public async Task Earlier_decisions_that_over_allocate_the_deposit_are_refused_not_hidden()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        DisputeTicket Decided(int hours)
        {
            var ticket = OpenTicket(booking, Build.Now.AddHours(hours));
            Assert.True(ticket.Resolve(DisputeResolution.Create(
                DepositDisposition.RefundEverything(Money.Jod(18m)).Value, null, null, "Recorded.", AdminId, Build.Now.AddHours(hours + 1)).Value).IsSuccess);
            return ticket;
        }
        context.GivenResolved(booking, Decided(4), Decided(6));
        var third = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(8)));

        var refused = await context.Admin().Handle(
            new ResolveDisputeCommand(third.Id, 0m, 0m, 0m, null, "Anything."), CancellationToken.None);
        var view = await context.Admin().Handle(new GetDisputeForReviewQuery(third.Id), CancellationToken.None);

        Assert.Equal("dispute.deposit_over_allocated", refused.Error.Code);
        Assert.Equal(ErrorKind.Conflict, refused.Error.Kind);
        Assert.Equal(0m, view.Value.DepositHeld.Amount);
        // The explaining figures are left out rather than stated wrong.
        Assert.Null(view.Value.DepositOnBooking);
        Assert.Null(view.Value.DecidedByEarlierTickets);
        Assert.Null(third.Resolution);
    }

    /// <summary>
    /// The office charge is bounded by the booking's range across its disputes too: a second ticket
    /// can only top up to the maximum, and the minimum binds only the first charge.
    /// </summary>
    [Fact]
    public async Task The_charge_to_the_office_stays_inside_the_range_across_disputes()
    {
        var context = new Context();
        var (booking, _) = Build.PaidBooking(customerId: CustomerId, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        booking.ClearDomainEvents();
        context.GivenBooking(booking);
        var penalty = booking.Penalty!;
        Assert.Same(BookingParty.Dealer, penalty.AttributedTo);
        var (min, max) = (penalty.MinAmount.Amount, penalty.MaxAmount.Amount);

        var first = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(first.Id, 18m, 0m, 0m, min, "The office cancelled late."), CancellationToken.None)).IsSuccess);
        context.GivenResolved(booking, first);

        var second = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(6)));
        var beyond = await context.Admin().Handle(
            new ResolveDisputeCommand(second.Id, 0m, 0m, 0m, max - min + 0.001m, "More."), CancellationToken.None);
        Assert.Equal("dispute.dealer_charge_out_of_range", beyond.Error.Code);

        // Below the floor is fine now: the floor bound only the first charge.
        var topUp = await context.Admin().Handle(
            new ResolveDisputeCommand(second.Id, 0m, 0m, 0m, 1m, "A small top-up."), CancellationToken.None);
        Assert.True(topUp.IsSuccess, topUp.IsFailure ? topUp.Error.Code : null);
    }

    [Fact]
    public async Task Once_the_office_was_charged_the_maximum_no_later_dispute_can_charge_it_again()
    {
        var context = new Context();
        var (booking, _) = Build.PaidBooking(customerId: CustomerId, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        booking.ClearDomainEvents();
        context.GivenBooking(booking);
        var max = booking.Penalty!.MaxAmount.Amount;

        var first = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(first.Id, 18m, 0m, 0m, max, "Full penalty."), CancellationToken.None)).IsSuccess);
        context.GivenResolved(booking, first);

        var second = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(6)));
        var again = await context.Admin().Handle(
            new ResolveDisputeCommand(second.Id, 0m, 0m, 0m, 0.001m, "Anything more."), CancellationToken.None);

        Assert.Equal("dispute.dealer_charge_out_of_range", again.Error.Code);
    }

    /// <summary>
    /// A second ticket is only reachable on a cancelled or no-show booking: resolving a dispute on a
    /// returned booking completes it, and a completed booking can no longer be disputed.
    /// </summary>
    [Fact]
    public async Task A_returned_booking_cannot_be_disputed_again_once_its_dispute_is_resolved()
    {
        var context = new Context();
        var booking = Build.ConfirmedBooking(customerId: CustomerId);
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End);
        booking.ClearDomainEvents();
        context.GivenBooking(booking);
        context.Clock.UtcNow = booking.Period.End.AddHours(1);

        var first = context.GivenTicket(OpenTicket(booking, booking.Period.End.AddHours(1)));
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(first.Id, 18m, 0m, 0m, null, "Refunded."), CancellationToken.None)).IsSuccess);
        Assert.Same(BookingStatus.Completed, booking.Status);

        var second = await context.Raise().Handle(
            new OpenDisputeCommand(CustomerId, booking.Id, "Again.", []), CancellationToken.None);
        Assert.Equal("dispute.booking_not_disputable", second.Error.Code);
    }

    /// <summary>
    /// Installed apps read this JSON: every field they know keeps its name and its shape, and the two
    /// new figures arrive beside them, additive.
    /// </summary>
    [Fact]
    public async Task The_dispute_json_keeps_every_field_an_installed_app_reads_and_adds_the_new_figures()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        await ResolvedFirst(context, booking, 9, 0, 9);
        var second = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(8)));

        var view = await context.Admin().Handle(new GetDisputeForReviewQuery(second.Id), CancellationToken.None);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(view.Value, Web));
        var root = json.RootElement;

        foreach (var name in new[] { "ticketId", "bookingId", "status", "isLive", "openedByParty", "reason", "openedAt", "slaDeadline", "isOverdue", "statements", "resolution", "booking" })
            Assert.True(root.TryGetProperty(name, out _), name);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("depositHeld").GetProperty("amount").ValueKind);
        Assert.Equal("JOD", root.GetProperty("depositHeld").GetProperty("currency").GetString());
        Assert.Equal(0m, root.GetProperty("depositHeld").GetProperty("amount").GetDecimal());
        Assert.Equal(18m, root.GetProperty("depositOnBooking").GetProperty("amount").GetDecimal());
        Assert.Equal(18m, root.GetProperty("decidedByEarlierTickets").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public void The_audit_line_of_a_second_dispute_names_the_nothing_it_split()
    {
        var resolution = DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(0m), Money.Jod(0m), Money.Jod(0m), Money.Jod(0m)).Value,
            null, null, "Already decided.", AdminId, Build.Now).Value;

        // At the currency's full scale, like every other amount on the platform (E2E F36), as parts (item 50).
        Assert.Equal(
            """{"currency":"JOD","held":"0.000","refund":"0.000","platform":"0.000","dealer":"0.000"}""",
            DisputeAuditor.Describe(resolution));
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

    // ── What the office hears of a decision (Fix & Polish Wave 3, C5) ────────────────────────────

    /// <summary>
    /// The office's team hears that Khadra decided, and — only because this decision moved a returned booking — that
    /// the booking completed. Both as Khadra's, with no administrator named.
    /// </summary>
    [Fact]
    public async Task A_decision_on_a_returned_booking_tells_the_office_it_was_decided_and_that_the_booking_completed()
    {
        var context = new Context();
        var office = context.Office(Id.New());
        var booking = Build.ConfirmedBooking(terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        var start = booking.Period.Start;
        booking.RecordPickup(BookingParty.Dealer, Id.New(), start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), start.AddDays(3));
        context.Dealers.GetByIdAsync(booking.DealerId, Arg.Any<CancellationToken>()).Returns(office);
        context.GivenBooking(booking);
        var ticket = context.GivenTicket(
            DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Overcharged for fuel.", TimeSpan.FromHours(48), start.AddDays(3).AddHours(1)).Value);
        context.Clock.UtcNow = start.AddDays(3).AddHours(6);
        var held = booking.Pricing.DepositAmount.Amount;

        await context.Admin().Handle(new ResolveDisputeCommand(ticket.Id, held, 0m, 0m, null, "Refund in full."), CancellationToken.None);

        var decided = context.Told.Where(told => told.Kind == NotificationKind.DisputeResolved).ToList();
        Assert.Equal(2, decided.Count);
        Assert.All(decided, told =>
        {
            Assert.Equal(ticket.Id, told.SubjectId);
            Assert.True(told.IsFromPlatform);
        });
        var completed = context.Told.Where(told => told.Kind == NotificationKind.BookingCompleted).ToList();
        Assert.Equal(2, completed.Count);
        Assert.All(completed, told => Assert.Equal(booking.Id, told.SubjectId));
    }

    /// <summary>A booking already over is not completed by the decision, so the office is not told it was.</summary>
    [Fact]
    public async Task A_decision_on_a_booking_already_over_announces_no_completion()
    {
        var context = new Context();
        var office = context.Office(Id.New());
        var booking = context.GivenBooking(CancelledAt(office));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var held = booking.Pricing.DepositAmount.Amount;

        var result = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held, 0m, 0m, null, "Refund in full."), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(2, context.Told.Count(told => told.Kind == NotificationKind.DisputeResolved));
        Assert.DoesNotContain(context.Told, told => told.Kind == NotificationKind.BookingCompleted);
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

        // The customer hears their dispute moved, and tapping it opens the TICKET.
        context.Notifier.Received(1).Raise(Arg.Is<Notification>(n =>
            n.Kind == NotificationKind.YourDisputeUpdated
            && n.RecipientUserId == booking.CustomerId
            && n.SubjectId == ticket.Id
            && n.SubjectReference == booking.Reference.Value));
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

    // ── People whose accounts no longer resolve ──────────────────────────────────────────────────
    //
    // The customer app receives this view, and prints each name as it arrives, so a name stays a
    // string -- an English stand-in when the account is gone. Beside it travels the fact, which is
    // what a console wording the case in its reader's language reads instead.

    private static readonly System.Text.Json.JsonSerializerOptions WireOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public async Task Each_person_on_a_dispute_carries_whether_their_account_still_resolves()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var formerStaff = Id.New();
        Assert.True(ticket.AddStatement(BookingParty.Dealer, formerStaff, "We waited an hour.", Build.Now.AddHours(5)).IsSuccess);
        Assert.True(ticket.AssignToAdmin(AdminId).IsSuccess);
        context.Names.NamesAsync(Arg.Any<IReadOnlyCollection<Id>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>
            {
                [CustomerId.Value] = "Layla Odeh",
                [AdminId.Value] = "Rania Haddad",
            });

        var view = await context.Composer().ComposeAsync(ticket, booking, BookingParty.Admin, CancellationToken.None);

        Assert.Equal("Layla Odeh", view.OpenedByName);
        Assert.False(view.OpenedByAccountClosed);
        Assert.Equal("Rania Haddad", view.AssignedAdminName);
        Assert.False(view.AssignedAdminAccountClosed);
        var opener = Assert.Single(view.Statements, statement => statement.AuthorUserId == CustomerId.Value);
        Assert.False(opener.AuthorAccountClosed);
        Assert.Equal("Layla Odeh", opener.AuthorName);
        var leaver = Assert.Single(view.Statements, statement => statement.AuthorUserId == formerStaff.Value);
        Assert.True(leaver.AuthorAccountClosed);
        Assert.Equal("Account closed", leaver.AuthorName);
    }

    /// <summary>
    /// Nobody holding a ticket and somebody holding it from a closed account are different facts. The
    /// name is null for the first only, and the flag is raised for the second only.
    /// </summary>
    [Fact]
    public async Task An_unassigned_ticket_is_not_mistaken_for_one_held_by_a_closed_account()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var unassigned = OpenTicket(booking, Build.Now.AddHours(4));
        var heldByLeaver = OpenTicket(booking, Build.Now.AddHours(4));
        Assert.True(heldByLeaver.AssignToAdmin(Id.New()).IsSuccess);

        var nobody = await context.Composer().ComposeAsync(unassigned, booking, BookingParty.Admin, CancellationToken.None);
        var leaver = await context.Composer().ComposeAsync(heldByLeaver, booking, BookingParty.Admin, CancellationToken.None);

        Assert.Null(nobody.AssignedAdminId);
        Assert.Null(nobody.AssignedAdminName);
        Assert.False(nobody.AssignedAdminAccountClosed);

        Assert.NotNull(leaver.AssignedAdminId);
        Assert.True(leaver.AssignedAdminAccountClosed);
        Assert.Equal("Account closed", leaver.AssignedAdminName);
    }

    /// <summary>
    /// The customer's copy names the platform and the office, never their people: the decision is Khadra's (owner
    /// decision D5 A, 2026-10-05), and an office's ticket and statements speak as the office (owner, 2026-10-06, Q4).
    /// The office's copy and the administrator's keep every name; the customer's own words keep theirs.
    /// </summary>
    [Fact]
    public async Task The_customer_reads_Khadra_and_the_office_never_a_named_person()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var staff = Id.New();
        var ticket = context.GivenTicket(DisputeTicket.Open(
            booking.Id, staff, BookingParty.Dealer, "The customer returned the car late.", TimeSpan.FromHours(48), Build.Now.AddHours(4)).Value);
        Assert.True(ticket.AddStatement(BookingParty.Customer, CustomerId, "I was on time.", Build.Now.AddHours(5)).IsSuccess);
        Assert.True(ticket.AssignToAdmin(AdminId).IsSuccess);
        var held = booking.Pricing.DepositAmount.Amount;
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held, 0m, 0m, null, "Refunded in full."), CancellationToken.None)).IsSuccess);
        context.Names.NamesAsync(Arg.Any<IReadOnlyCollection<Id>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>
            {
                [CustomerId.Value] = "Layla Odeh",
                [AdminId.Value] = "Rania Haddad",
                [staff.Value] = "Sami Khoury",
            });

        var customer = await context.Composer().ComposeAsync(ticket, booking, BookingParty.Customer, CancellationToken.None);
        var office = await context.Composer().ComposeAsync(ticket, booking, BookingParty.Dealer, CancellationToken.None);
        var admin = await context.Composer().ComposeAsync(ticket, booking, BookingParty.Admin, CancellationToken.None);

        var officeName = customer.Booking.DealerName;
        Assert.Equal(officeName, customer.OpenedByName);
        Assert.Null(customer.OpenedByUserId);
        var officeStatement = Assert.Single(customer.Statements, statement => statement.Party == "Dealer");
        Assert.Equal(officeName, officeStatement.AuthorName);
        Assert.Null(officeStatement.AuthorUserId);
        Assert.Equal("Khadra", customer.AssignedAdminName);
        Assert.Null(customer.AssignedAdminId);
        Assert.Equal("Khadra", customer.Resolution!.ResolvedByName);
        Assert.Null(customer.Resolution.ResolvedByAdminId);
        var own = Assert.Single(customer.Statements, statement => statement.Party == "Customer");
        Assert.Equal("Layla Odeh", own.AuthorName);
        Assert.Equal(CustomerId.Value, own.AuthorUserId);
        // The customer's history carries nobody's id but their own.
        Assert.All(customer.Booking.History.Where(change => change.ActorParty != "Customer"), change => Assert.Null(change.ActorUserId));

        foreach (var internalCopy in new[] { office, admin })
        {
            Assert.Equal("Sami Khoury", internalCopy.OpenedByName);
            Assert.Equal(staff.Value, internalCopy.OpenedByUserId);
            Assert.Equal("Rania Haddad", internalCopy.Resolution!.ResolvedByName);
            Assert.Equal(AdminId.Value, internalCopy.Resolution.ResolvedByAdminId);
        }
        Assert.Equal("Rania Haddad", admin.AssignedAdminName);
    }

    /// <summary>A closed ticket is still on the booking, so a decision page is never reachable only by its address (F44).</summary>
    [Fact]
    public async Task A_closed_dispute_stays_listed_on_the_booking_it_decided()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var listed = new BookingDisputeDto(ticket.Id.Value, "Resolved", ticket.OpenedAt, Build.Now.AddHours(6));
        context.BookingReader.ContextAsync(booking.Id, Arg.Any<CancellationToken>())
            .Returns(new BookingContext(null, "Petra Wheels", false, null, "Layla Odeh", false, null, null, Disputes: [listed]));

        var customer = await context.Composer().ComposeAsync(ticket, booking, BookingParty.Customer, CancellationToken.None);

        Assert.Equal([listed], customer.Booking.Disputes!);
    }

    [Fact]
    public async Task A_resolution_says_when_the_administrator_who_made_it_has_since_left()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var held = booking.Pricing.DepositAmount.Amount;
        var resolved = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held, 0m, 0m, null, "Refunded in full."),
            CancellationToken.None);
        Assert.True(resolved.IsSuccess);

        // The context resolves no names at all, so the admin who decided is gone by the time it is read.
        var view = await context.Composer().ComposeAsync(ticket, booking, BookingParty.Admin, CancellationToken.None);

        Assert.True(view.Resolution!.ResolvedByAccountClosed);
        Assert.Equal("Account closed", view.Resolution.ResolvedByName);
    }

    /// <summary>
    /// The names the customer app reads keep their JSON names and types; the flags sit beside them.
    /// </summary>
    [Fact]
    public async Task The_wire_keeps_every_name_a_string_and_adds_the_flags_beside_them()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        Assert.True(ticket.AddStatement(BookingParty.Customer, CustomerId, "Still waiting.", Build.Now.AddHours(5)).IsSuccess);
        var held = booking.Pricing.DepositAmount.Amount;
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held, 0m, 0m, null, "Refunded in full."),
            CancellationToken.None)).IsSuccess);

        var view = await context.Composer().ComposeAsync(ticket, booking, BookingParty.Admin, CancellationToken.None);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(view, WireOptions));
        var root = json.RootElement;

        Assert.Equal(System.Text.Json.JsonValueKind.String, root.GetProperty("openedByName").ValueKind);
        Assert.True(root.GetProperty("openedByAccountClosed").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("assignedAdminName").ValueKind);
        Assert.False(root.GetProperty("assignedAdminAccountClosed").GetBoolean());

        var statement = root.GetProperty("statements")[0];
        Assert.Equal(System.Text.Json.JsonValueKind.String, statement.GetProperty("authorName").ValueKind);
        Assert.True(statement.GetProperty("authorAccountClosed").GetBoolean());

        var resolution = root.GetProperty("resolution");
        Assert.Equal(System.Text.Json.JsonValueKind.String, resolution.GetProperty("resolvedByName").ValueKind);
        Assert.True(resolution.GetProperty("resolvedByAccountClosed").GetBoolean());
    }

    // ── Who is shown which share (owner decision 3, 2026-09-26; pre-launch item 151) ─────────────
    //
    // The rental office is shown the basis a decision split, its own share and any charge assessed to
    // it, and nothing of the customer's or the platform's shares. The customer's copy is unchanged: it
    // is the installed app's contract, and the owner deferred its half of item 151 (2026-09-27).

    /// <summary>A customer-penalty booking whose dispute was split three ways: 9, 2 and 7 of 18.</summary>
    private static async Task<(Context Context, Booking Booking, DisputeTicket Ticket, decimal Held, decimal ToOffice)> SplitThreeWaysAsync()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var held = booking.Pricing.DepositAmount.Amount;
        var toOffice = held - (held / 2) - 2m;
        var resolved = await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held / 2, 2m, toOffice, null, "Both sides partly at fault."),
            CancellationToken.None);
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error.Code : null);
        return (context, booking, ticket, held, toOffice);
    }

    [Fact]
    public async Task The_office_is_shown_the_basis_and_its_own_share_and_never_the_customers_or_the_platforms()
    {
        var (context, booking, ticket, held, toOffice) = await SplitThreeWaysAsync();
        // The customer's refund row is on the booking too; the office's copy of the booking drops it (4b).
        context.BookingReader.ContextAsync(booking.Id, Arg.Any<CancellationToken>())
            .Returns(new BookingContext(null, "Petra Wheels", false, null, "Layla Odeh", false, null, null,
                Refunds: [new RefundDto(Guid.NewGuid(), Guid.NewGuid(), "DisputeResolution", MoneyDto.From(Money.Jod(held / 2)), "Requested", Build.Now, null, null, null, ticket.Id.Value)],
                HasResolvedDispute: true));

        var office = await context.Composer().ComposeAsync(ticket, booking, BookingParty.Dealer, CancellationToken.None);

        var decision = office.Resolution!;
        Assert.Equal(held, decision.DepositHeld.Amount);
        Assert.Equal(toOffice, decision.TransferredToDealer!.Amount);
        Assert.Null(decision.DealerCharge);
        Assert.Null(decision.RefundToCustomer);
        Assert.Null(decision.RetainedByPlatform);
        Assert.Null(decision.WaivesEverything);
        Assert.Equal("Both sides partly at fault.", decision.Note);
        Assert.Equal(held, office.DepositHeld.Amount);
        Assert.Null(office.Booking.Refunds);
    }

    /// <summary>
    /// The customer's copy keeps the refund the decision gave them, tied to the ticket — the website reads its status
    /// from exactly this row (Fix &amp; Polish W1-5, E2E F43), so the dispute payload needed no field of its own.
    /// </summary>
    [Fact]
    public async Task The_customer_is_given_the_refund_their_decision_created_tied_to_the_ticket()
    {
        var (context, booking, ticket, held, _) = await SplitThreeWaysAsync();
        var refundId = Guid.NewGuid();
        context.BookingReader.ContextAsync(booking.Id, Arg.Any<CancellationToken>())
            .Returns(new BookingContext(null, "Petra Wheels", false, null, "Layla Odeh", false, null, null,
                Refunds: [new RefundDto(refundId, Guid.NewGuid(), "DisputeResolution", MoneyDto.From(Money.Jod(held / 2)), "Settled", Build.Now, Build.Now, Build.Now, null, ticket.Id.Value)],
                HasResolvedDispute: true));

        var customer = await context.Composer().ComposeAsync(ticket, booking, BookingParty.Customer, CancellationToken.None);

        var refund = Assert.Single(customer.Booking.Refunds!);
        Assert.Equal(refundId, refund.RefundId);
        Assert.Equal(ticket.Id.Value, refund.DisputeTicketId);
        Assert.Equal("Settled", refund.Status);
        Assert.NotNull(refund.SettledAt);
    }

    /// <summary>
    /// Through the handler, not the composer: "my dispute" is the one path that can hand the office a
    /// decision (a resolved ticket takes no statement and cannot be withdrawn), and it must pass the
    /// office as the reader.
    /// </summary>
    [Fact]
    public async Task Reading_its_dispute_the_office_gets_its_own_copy_of_the_decision()
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
        var held = booking.Pricing.DepositAmount.Amount;
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held / 2, 2m, held - (held / 2) - 2m, null, "Both sides partly at fault."),
            CancellationToken.None)).IsSuccess);

        var office = await context.Raise().Handle(new GetMyDisputeQuery(OwnerId, ticket.Id), CancellationToken.None);
        var customer = await context.Raise().Handle(new GetMyDisputeQuery(CustomerId, ticket.Id), CancellationToken.None);

        Assert.True(office.IsSuccess, office.IsFailure ? office.Error.Code : null);
        Assert.Null(office.Value.Resolution!.RefundToCustomer);
        Assert.Null(office.Value.Resolution.RetainedByPlatform);
        Assert.Equal(held - (held / 2) - 2m, office.Value.Resolution.TransferredToDealer!.Amount);
        Assert.Equal(held / 2, customer.Value.Resolution!.RefundToCustomer!.Amount);
    }

    [Fact]
    public async Task The_office_is_shown_a_charge_assessed_to_it()
    {
        var context = new Context();
        var (booking, _) = Build.PaidBooking(customerId: CustomerId, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        booking.ClearDomainEvents();
        context.GivenBooking(booking);
        var charge = booking.Penalty!.MinAmount.Amount;
        var held = booking.Pricing.DepositAmount.Amount;
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held, 0m, 0m, charge, "The office cancelled late."), CancellationToken.None)).IsSuccess);

        var decision = (await context.Composer().ComposeAsync(ticket, booking, BookingParty.Dealer, CancellationToken.None)).Resolution!;

        Assert.Equal(charge, decision.DealerCharge!.Amount);
        Assert.Equal(0m, decision.TransferredToDealer!.Amount);
        Assert.Null(decision.RefundToCustomer);
    }

    private static ClientInfo App(string version) => new("1.2.3.4", "Dart/3.5", AppVersion.Parse(version));

    /// <summary>
    /// Owner decision 3, carried to the customer's dispute page (pre-launch item 151; Wave 7): the customer is shown the
    /// basis and their own share, never the office's or the platform's, nor what the office was charged or the waiver
    /// flag read from those. The website declares no version and the app from 1.4.0 knows not to expect them.
    /// </summary>
    [Fact]
    public async Task The_customer_is_shown_only_their_own_share_of_a_decision()
    {
        var (context, booking, ticket, held, _) = await SplitThreeWaysAsync();

        foreach (var client in new[] { ClientInfo.Unknown, App("1.4.0"), App("1.4.0+7"), App("1.10.0") })
        {
            var decision = (await context.Composer().ComposeAsync(ticket, booking, BookingParty.Customer, client, CancellationToken.None)).Resolution!;

            Assert.Equal(held, decision.DepositHeld.Amount);
            Assert.Equal(held / 2, decision.RefundToCustomer!.Amount);
            Assert.Null(decision.RetainedByPlatform);
            Assert.Null(decision.TransferredToDealer);
            Assert.Null(decision.DealerCharge);
            Assert.Null(decision.WaivesEverything);
            Assert.Equal("Both sides partly at fault.", decision.Note);
        }
    }

    /// <summary>
    /// An installed build older than 1.4.0 renders the office's and the platform's rows and would show a share it was no
    /// longer sent as zero, so it is still sent them until the minimum refuses it outright (a temporary bridge; item 239).
    /// The administrator's copy carries every share, always.
    /// </summary>
    [Fact]
    public async Task An_app_build_older_than_the_release_and_the_administrator_still_read_every_share()
    {
        var (context, booking, ticket, held, toOffice) = await SplitThreeWaysAsync();

        var older = await context.Composer().ComposeAsync(ticket, booking, BookingParty.Customer, App("1.3.0"), CancellationToken.None);
        var admin = await context.Composer().ComposeAsync(ticket, booking, BookingParty.Admin, CancellationToken.None);

        foreach (var decision in new[] { older.Resolution!, admin.Resolution! })
        {
            Assert.Equal(held / 2, decision.RefundToCustomer!.Amount);
            Assert.Equal(2m, decision.RetainedByPlatform!.Amount);
            Assert.Equal(toOffice, decision.TransferredToDealer!.Amount);
        }

        Assert.False(admin.Resolution!.WaivesEverything);
    }

    /// <summary>What the office was charged is never the customer's, on any build: none renders it.</summary>
    [Fact]
    public async Task The_customer_is_never_told_what_the_office_was_charged()
    {
        var context = new Context();
        var (booking, _) = Build.PaidBooking(customerId: CustomerId, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        booking.ClearDomainEvents();
        context.GivenBooking(booking);
        var held = booking.Pricing.DepositAmount.Amount;
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held, 0m, 0m, booking.Penalty!.MinAmount.Amount, "The office cancelled late."),
            CancellationToken.None)).IsSuccess);

        foreach (var client in new[] { ClientInfo.Unknown, App("1.3.0"), App("1.4.0+7") })
        {
            var decision = (await context.Composer().ComposeAsync(ticket, booking, BookingParty.Customer, client, CancellationToken.None)).Resolution!;

            Assert.Null(decision.DealerCharge);
            Assert.Null(decision.WaivesEverything);
            Assert.Equal(held, decision.RefundToCustomer!.Amount);
        }
    }

    /// <summary>Through the handlers, which pass the reader's client, so the controller's version reaches the copy.</summary>
    [Fact]
    public async Task Reading_their_dispute_the_customer_gets_the_copy_their_build_can_read()
    {
        var context = new Context();
        var booking = Build.Booking(customerId: CustomerId, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        booking.Approve(Id.New(), Build.Now.AddMinutes(10));
        booking.ConfirmDepositPaid(Id.New(), Build.Now);
        booking.Cancel(BookingParty.Customer, CustomerId, "Changed plans.", Build.Now.AddHours(3));
        context.GivenBooking(booking);
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var held = booking.Pricing.DepositAmount.Amount;
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held / 2, 2m, held - (held / 2) - 2m, null, "Both sides partly at fault."),
            CancellationToken.None)).IsSuccess);

        var current = await context.Raise().Handle(new GetMyDisputeQuery(CustomerId, ticket.Id, App("1.4.0+7")), CancellationToken.None);
        var website = await context.Raise().Handle(new GetMyDisputeQuery(CustomerId, ticket.Id), CancellationToken.None);
        var older = await context.Raise().Handle(new GetMyDisputeQuery(CustomerId, ticket.Id, App("1.3.0")), CancellationToken.None);

        Assert.Null(current.Value.Resolution!.RetainedByPlatform);
        Assert.Null(current.Value.Resolution.TransferredToDealer);
        Assert.Null(website.Value.Resolution!.RetainedByPlatform);
        Assert.Equal(2m, older.Value.Resolution!.RetainedByPlatform!.Amount);
        Assert.Equal(held / 2, current.Value.Resolution.RefundToCustomer!.Amount);
    }

    /// <summary>
    /// A decision that gives the customer everything and charges nobody: its waiver flag would tell the office the
    /// customer's share, so the office reads null there. The customer's copy reads null for every share but their own,
    /// keeping each name the installed app parses; an older build still reads the figures (item 239).
    /// </summary>
    [Fact]
    public async Task On_the_wire_each_reader_gets_null_where_a_share_is_not_theirs()
    {
        var context = new Context();
        var booking = context.GivenBooking(CancelledBooking(Build.Now));
        var ticket = context.GivenTicket(OpenTicket(booking, Build.Now.AddHours(4)));
        var held = booking.Pricing.DepositAmount.Amount;
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, held, 0m, 0m, null, "Refunded in full."), CancellationToken.None)).IsSuccess);

        JsonElement WireOf(Khadra.Application.Disputes.Dtos.DisputeDto view) =>
            JsonDocument.Parse(JsonSerializer.Serialize(view, WireOptions)).RootElement.GetProperty("resolution").Clone();
        var office = WireOf(await context.Composer().ComposeAsync(ticket, booking, BookingParty.Dealer, CancellationToken.None));
        var customer = WireOf(await context.Composer().ComposeAsync(ticket, booking, BookingParty.Customer, App("1.4.0+7"), CancellationToken.None));
        var older = WireOf(await context.Composer().ComposeAsync(ticket, booking, BookingParty.Customer, App("1.3.0"), CancellationToken.None));

        Assert.Equal(JsonValueKind.Null, office.GetProperty("refundToCustomer").ValueKind);
        Assert.Equal(JsonValueKind.Null, office.GetProperty("retainedByPlatform").ValueKind);
        Assert.Equal(JsonValueKind.Null, office.GetProperty("waivesEverything").ValueKind);
        Assert.Equal(held, office.GetProperty("depositHeld").GetProperty("amount").GetDecimal());
        Assert.Equal(0m, office.GetProperty("transferredToDealer").GetProperty("amount").GetDecimal());

        Assert.Equal(held, customer.GetProperty("refundToCustomer").GetProperty("amount").GetDecimal());
        Assert.Equal(held, customer.GetProperty("depositHeld").GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, customer.GetProperty("retainedByPlatform").ValueKind);
        Assert.Equal(JsonValueKind.Null, customer.GetProperty("transferredToDealer").ValueKind);
        Assert.Equal(JsonValueKind.Null, customer.GetProperty("dealerCharge").ValueKind);
        Assert.Equal(JsonValueKind.Null, customer.GetProperty("waivesEverything").ValueKind);

        Assert.Equal(0m, older.GetProperty("retainedByPlatform").GetProperty("amount").GetDecimal());
        Assert.Equal(0m, older.GetProperty("transferredToDealer").GetProperty("amount").GetDecimal());
    }
    // ── The decision's preview (Wave 2 C1; E2E F37) ──────────────────────────────────────────────

    /// <summary>
    /// A deposit-paid booking collected and returned, its window still open: E3's shape on Staging. 18 deposit,
    /// 6 frozen commission, nothing paid above the deposit. The clock is an hour after the return.
    /// </summary>
    private static Booking ReturnedBooking(Context context)
    {
        var (booking, _) = Build.PaidBooking(customerId: CustomerId, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start).IsSuccess);
        Assert.True(booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End).IsSuccess);
        booking.ClearDomainEvents();
        context.Clock.UtcNow = booking.Period.End.AddHours(1);
        return context.GivenBooking(booking);
    }

    /// <summary>A paid booking the OFFICE cancelled late: a penalty against the office, the deposit held.</summary>
    private static Booking CancelledByTheOffice(Context context)
    {
        var (booking, _) = Build.PaidBooking(customerId: CustomerId, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        booking.ClearDomainEvents();
        Assert.Same(BookingParty.Dealer, booking.Penalty!.AttributedTo);
        context.Clock.UtcNow = booking.FinishedAt!.Value.AddHours(2);
        return context.GivenBooking(booking);
    }

    private static PreviewDisputeResolutionQuery Preview(Id ticketId, decimal refund, decimal platform, decimal dealer, decimal? charge = null, string? note = null) =>
        new(ticketId, refund, platform, dealer, charge, note);

    /// <summary>
    /// E2E F37: the administrator set Khadra's part explicitly, and the office's share was then cut by commission at
    /// finality without anything saying so. The preview says it, in the ledger's own figures, before the decision.
    /// </summary>
    [Fact]
    public async Task The_preview_shows_the_office_its_share_and_the_commission_taken_from_it()
    {
        var context = new Context();
        var booking = ReturnedBooking(context);
        var ticket = context.GivenTicket(OpenTicket(booking, booking.Period.End.AddMinutes(30)));

        var result = await context.Admin().Handle(Preview(ticket.Id, 10m, 4m, 4m), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var preview = result.Value;
        Assert.Equal("Completed", preview.StatusAfter);
        Assert.Equal("Final", preview.OfficeState);
        Assert.Equal("RentalAfterDispute", preview.Outcome);
        Assert.Equal(
            [("DisputeShare", 4m, (Guid?)ticket.Id.Value), ("Commission", 4m, null)],
            preview.Lines.Select(line => (line.Kind, line.Amount.Amount, line.TicketId)));
        Assert.Equal(10m, preview.Customer.RefundRequested.Amount);
        Assert.Equal(4m, preview.Platform.RetainedShare.Amount);
        Assert.Equal(4m, preview.Platform.Commission.Amount);
        Assert.Equal(4m, preview.Office.Share.Amount);
        Assert.Equal(4m, preview.Office.Money.Amount);
        // The frozen figure and what is taken, separately: the cap is the office's money (owner, 2026-09-29).
        Assert.Equal(6m, preview.Office.FrozenCommission.Amount);
        Assert.Equal(4m, preview.Office.Commission.Amount);
        Assert.Equal(0m, preview.Office.Charges.Amount);
        Assert.Equal(0m, preview.Office.Net.Amount);
        Assert.Null(preview.EarlierDecisions);
        // A returned booking is completed by the decision: final at it, and recorded no sooner than the margin after.
        Assert.Null(preview.FurtherDecisionsPossibleUntil);
        Assert.Equal(context.Clock.UtcNow.Add(context.PayablesSettings.FinalityMargin), preview.RecordedNotBefore);
        Assert.Equal(2, preview.CalculatorVersion);
        // At the currency's full scale on the wire (E2E F36), the net included.
        Assert.Equal("4.000", preview.Office.Share.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("0.000", preview.Office.Net.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task The_preview_writes_nothing_and_needs_no_note()
    {
        var context = new Context();
        var booking = ReturnedBooking(context);
        var ticket = context.GivenTicket(OpenTicket(booking, booking.Period.End.AddMinutes(30)));

        var silent = await context.Admin().Handle(Preview(ticket.Id, 18m, 0m, 0m), CancellationToken.None);
        var blank = await context.Admin().Handle(Preview(ticket.Id, 18m, 0m, 0m, note: "   "), CancellationToken.None);

        Assert.True(silent.IsSuccess, silent.IsFailure ? silent.Error.Code : null);
        Assert.True(blank.IsSuccess, blank.IsFailure ? blank.Error.Code : null);
        Assert.True(ticket.Status.IsLive);
        Assert.Null(ticket.Resolution);
        Assert.Same(BookingStatus.Returned, booking.Status);
        Assert.Empty(context.Audited);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        context.Notifier.DidNotReceiveWithAnyArgs().Raise(default!);
    }

    /// <summary>One drafter: what resolving refuses, the preview refuses, with the same code.</summary>
    [Theory]
    [InlineData(9, 0, 0, null, "dispute.disposition_unbalanced")]
    [InlineData(17.9995, 0.0005, 0, null, "dispute.amount_precision")]
    [InlineData(18, 0, 0, 1.0, "dispute.dealer_charge_unassessed")]
    public async Task The_preview_refuses_what_resolving_refuses(double refund, double platform, double dealer, double? charge, string expected)
    {
        var context = new Context();
        var booking = ReturnedBooking(context);
        var ticket = context.GivenTicket(OpenTicket(booking, booking.Period.End.AddMinutes(30)));
        var (r, p, d, c) = ((decimal)refund, (decimal)platform, (decimal)dealer, (decimal?)charge);

        var previewed = await context.Admin().Handle(Preview(ticket.Id, r, p, d, c), CancellationToken.None);
        var resolved = await context.Admin().Handle(new ResolveDisputeCommand(ticket.Id, r, p, d, c, "Decided."), CancellationToken.None);

        Assert.Equal(expected, previewed.Error.Code);
        Assert.Equal(expected, resolved.Error.Code);
        Assert.True(ticket.Status.IsLive);
    }

    [Fact]
    public async Task A_closed_or_missing_ticket_is_refused_for_what_it_is_not_for_its_amounts()
    {
        var context = new Context();
        var booking = ReturnedBooking(context);
        var withdrawn = context.GivenTicket(OpenTicket(booking, booking.Period.End.AddMinutes(30)));
        Assert.True(withdrawn.Withdraw(CustomerId, booking.Period.End.AddMinutes(40)).IsSuccess);

        // Amounts that would not balance either way: the ticket's state answers first.
        var previewed = await context.Admin().Handle(Preview(withdrawn.Id, 1m, 0m, 0m), CancellationToken.None);
        var resolved = await context.Admin().Handle(new ResolveDisputeCommand(withdrawn.Id, 1m, 0m, 0m, null, "No."), CancellationToken.None);
        var missing = await context.Admin().Handle(Preview(Id.New(), 18m, 0m, 0m), CancellationToken.None);

        Assert.Equal("dispute.already_withdrawn", previewed.Error.Code);
        Assert.Equal("dispute.already_withdrawn", resolved.Error.Code);
        Assert.Equal("dispute.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task A_cancellations_preview_says_another_dispute_may_change_it_until_its_window_closes()
    {
        var context = new Context();
        var booking = CancelledByTheOffice(context);
        var ticket = context.GivenTicket(OpenTicket(booking, booking.FinishedAt!.Value.AddHours(1)));
        var windowEnds = booking.DisputeWindowEndsAt!.Value;
        var charge = booking.Penalty!.MinAmount.Amount;

        var result = await context.Admin().Handle(Preview(ticket.Id, 18m, 0m, 0m, charge), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var preview = result.Value;
        Assert.Equal("Cancelled", preview.StatusAfter);
        Assert.Equal("DisputeDecided", preview.Outcome);
        // No share, so no commission; the charge is taken from nothing, and the office owes it.
        Assert.Equal([("DisputeCharge", charge, (Guid?)ticket.Id.Value)], preview.Lines.Select(line => (line.Kind, line.Amount.Amount, line.TicketId)));
        Assert.Equal(-charge, preview.Office.Net.Amount);
        Assert.Equal(windowEnds, preview.FurtherDecisionsPossibleUntil);
        Assert.Equal(windowEnds.Add(context.PayablesSettings.FinalityMargin), preview.RecordedNotBefore);
    }

    [Fact]
    public async Task The_preview_includes_what_earlier_disputes_on_the_booking_decided()
    {
        var context = new Context();
        var booking = CancelledByTheOffice(context);
        var charge = booking.Penalty!.MinAmount.Amount;
        var first = context.GivenTicket(OpenTicket(booking, booking.FinishedAt!.Value.AddMinutes(30)));
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(first.Id, 18m, 0m, 0m, charge, "The office cancelled late."), CancellationToken.None)).IsSuccess);
        context.GivenResolved(booking, first);
        var second = context.GivenTicket(OpenTicket(booking, booking.FinishedAt.Value.AddHours(1)));

        var result = await context.Admin().Handle(Preview(second.Id, 0m, 0m, 0m, 1m), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var preview = result.Value;
        var earlier = preview.EarlierDecisions!;
        Assert.Equal(1, earlier.Count);
        Assert.Equal(18m, earlier.ToCustomer.Amount);
        Assert.Equal(charge, earlier.ChargedToOffice.Amount);
        Assert.Equal(
            [("DisputeCharge", charge, (Guid?)first.Id.Value), ("DisputeCharge", 1m, second.Id.Value)],
            preview.Lines.Select(line => (line.Kind, line.Amount.Amount, line.TicketId)));
        Assert.Equal(charge + 1m, preview.Office.Charges.Amount);
        Assert.Equal(-(charge + 1m), preview.Office.Net.Amount);
    }

    /// <summary>
    /// The ledger holds a booking whose records contradict one another for review, whatever a decision says (advisor's
    /// review of Wave 2). The preview runs the ledger's own check, so it says so rather than promising a recording.
    /// </summary>
    [Fact]
    public async Task The_preview_says_when_the_ledger_will_hold_the_booking_instead_of_recording_it()
    {
        var context = new Context();
        var (booking, payment) = Build.PaidBooking(customerId: CustomerId, terms: Build.Terms(settlementWindow: TimeSpan.FromDays(7)));
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start).IsSuccess);
        Assert.True(booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End).IsSuccess);
        context.Clock.UtcNow = booking.Period.End.AddHours(1);
        context.GivenBooking(booking);
        var ticket = context.GivenTicket(OpenTicket(booking, booking.Period.End.AddMinutes(30)));

        // Its records agree: nothing to hold it for.
        context.Payments.ListForBookingAsync(booking.Id, Arg.Any<CancellationToken>()).Returns([payment]);
        var agreeing = await context.Admin().Handle(Preview(ticket.Id, 18m, 0m, 0m), CancellationToken.None);
        Assert.True(agreeing.IsSuccess, agreeing.IsFailure ? agreeing.Error.Code : null);
        Assert.Empty(agreeing.Value.LedgerIssues);

        // The payment that confirmed it is missing from its records: the ledger would hold it, and the preview says why.
        context.Payments.ListForBookingAsync(booking.Id, Arg.Any<CancellationToken>()).Returns([]);
        var contradicting = await context.Admin().Handle(Preview(ticket.Id, 18m, 0m, 0m), CancellationToken.None);
        Assert.True(contradicting.IsSuccess, contradicting.IsFailure ? contradicting.Error.Code : null);
        Assert.Equal(["ConfirmingPaymentMissing"], contradicting.Value.LedgerIssues);
    }

    /// <summary>
    /// A decision made after a cancellation's window closed: the booking is final already, so the ledger records it once
    /// its margin after the window has passed, and on its very next pass if that has passed too. The floor is never
    /// "now plus the margin", which the ledger does not wait for (advisor's review of Wave 2).
    /// </summary>
    [Fact]
    public async Task A_decision_after_the_window_closed_is_recorded_when_the_margin_after_the_window_passes()
    {
        var context = new Context();
        var booking = CancelledByTheOffice(context);
        var ticket = context.GivenTicket(OpenTicket(booking, booking.FinishedAt!.Value.AddHours(1)));
        var windowEnds = booking.DisputeWindowEndsAt!.Value;
        var margin = context.PayablesSettings.FinalityMargin;
        var charge = booking.Penalty!.MinAmount.Amount;

        // Inside the margin: the floor is the window's end plus the margin, not now plus it.
        context.Clock.UtcNow = windowEnds.Add(margin / 2);
        var inside = (await context.Admin().Handle(Preview(ticket.Id, 18m, 0m, 0m, charge), CancellationToken.None)).Value;
        Assert.Equal(windowEnds.Add(margin), inside.RecordedNotBefore);
        Assert.False(inside.RecordedAtNextPass);
        // The window has closed: no further dispute can be opened on the booking.
        Assert.Null(inside.FurtherDecisionsPossibleUntil);

        // Past it: the next pass records it.
        context.Clock.UtcNow = windowEnds.Add(margin * 3);
        var past = (await context.Admin().Handle(Preview(ticket.Id, 18m, 0m, 0m, charge), CancellationToken.None)).Value;
        Assert.Equal(context.Clock.UtcNow, past.RecordedNotBefore);
        Assert.True(past.RecordedAtNextPass);
        Assert.Null(past.FurtherDecisionsPossibleUntil);
    }

    // ── The office's expected outcome (Wave 2 C1) ────────────────────────────────────────────────

    /// <summary>
    /// A second dispute still open on the booking will change the office's figures however long ago the window closed
    /// (an SLA can outlast a window), and the office's copy of the first says so (advisor's review of Wave 2).
    /// </summary>
    [Fact]
    public async Task The_offices_copy_says_when_another_dispute_on_the_booking_is_still_open()
    {
        var context = new Context();
        var booking = CancelledByTheOffice(context);
        var first = context.GivenTicket(OpenTicket(booking, booking.FinishedAt!.Value.AddHours(1)));
        var charge = booking.Penalty!.MinAmount.Amount;
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(first.Id, 18m, 0m, 0m, charge, "The office cancelled late."), CancellationToken.None)).IsSuccess);
        context.GivenResolved(booking, first);
        context.Clock.UtcNow = booking.DisputeWindowEndsAt!.Value.AddHours(1);

        var alone = (await context.Composer().ComposeAsync(first, booking, BookingParty.Dealer, CancellationToken.None)).ExpectedOutcome!;
        Assert.False(alone.AnotherDisputeOpen);
        Assert.Null(alone.FurtherDecisionsPossibleUntil);

        context.Tickets.HasLiveTicketAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(true);
        var withAnother = (await context.Composer().ComposeAsync(first, booking, BookingParty.Dealer, CancellationToken.None)).ExpectedOutcome!;
        Assert.Equal(OfficeOutcomeSources.Projected, withAnother.Source);
        Assert.True(withAnother.AnotherDisputeOpen);
    }

    [Fact]
    public async Task The_offices_copy_of_a_decided_dispute_projects_what_it_comes_to_until_the_ledger_records_it()
    {
        var context = new Context();
        var booking = CancelledByTheOffice(context);
        var ticket = context.GivenTicket(OpenTicket(booking, booking.FinishedAt!.Value.AddHours(1)));
        var live = await context.Composer().ComposeAsync(ticket, booking, BookingParty.Dealer, CancellationToken.None);
        Assert.Null(live.ExpectedOutcome);
        var charge = booking.Penalty!.MinAmount.Amount;

        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, 18m, 0m, 0m, charge, "The office cancelled late."), CancellationToken.None)).IsSuccess);
        context.GivenResolved(booking, ticket);

        var office = (await context.Composer().ComposeAsync(ticket, booking, BookingParty.Dealer, CancellationToken.None)).ExpectedOutcome!;
        Assert.Equal(OfficeOutcomeSources.Projected, office.Source);
        Assert.Null(office.PayableId);
        Assert.Equal("DisputeDecided", office.Outcome);
        Assert.Equal(-charge, office.Net.Amount);
        Assert.Equal(charge, office.Charges.Amount);
        Assert.Equal(booking.DisputeWindowEndsAt, office.FinalAt);
        // The window is still open: another dispute may yet change it.
        Assert.Equal(booking.DisputeWindowEndsAt, office.FurtherDecisionsPossibleUntil);

        // Nobody else is given it.
        Assert.Null((await context.Composer().ComposeAsync(ticket, booking, BookingParty.Customer, CancellationToken.None)).ExpectedOutcome);
        Assert.Null((await context.Composer().ComposeAsync(ticket, booking, BookingParty.Admin, CancellationToken.None)).ExpectedOutcome);
    }

    [Fact]
    public async Task Once_the_ledger_recorded_the_booking_the_offices_copy_is_the_recorded_payable()
    {
        var context = new Context();
        var booking = CancelledByTheOffice(context);
        var ticket = context.GivenTicket(OpenTicket(booking, booking.FinishedAt!.Value.AddHours(1)));
        var charge = booking.Penalty!.MinAmount.Amount;
        Assert.True((await context.Admin().Handle(
            new ResolveDisputeCommand(ticket.Id, 18m, 0m, 0m, charge, "The office cancelled late."), CancellationToken.None)).IsSuccess);
        context.GivenResolved(booking, ticket);
        var payable = Khadra.Domain.Payables.OfficePayable.Record(
            new Khadra.Domain.Payables.PayableDraft(
                booking.Id,
                booking.DealerId,
                booking.Reference.Value,
                "JOD",
                "TestProvider",
                Khadra.Domain.Payables.PayableOutcome.DisputeDecided,
                booking.DisputeWindowEndsAt!.Value,
                2,
                [new(Khadra.Domain.Payables.PayableLineKind.DisputeCharge, charge, ticket.Id)]),
            booking.DisputeWindowEndsAt.Value.AddMinutes(10));
        context.Payables.GetByBookingAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(payable);

        var office = (await context.Composer().ComposeAsync(ticket, booking, BookingParty.Dealer, CancellationToken.None)).ExpectedOutcome!;

        Assert.Equal(OfficeOutcomeSources.Recorded, office.Source);
        Assert.Equal(payable.Id.Value, office.PayableId);
        Assert.Equal(payable.Net, office.Net.Amount);
        Assert.Equal(payable.FinalAt, office.FinalAt);
        Assert.Null(office.FurtherDecisionsPossibleUntil);
        Assert.Equal([("DisputeCharge", charge, (Guid?)ticket.Id.Value)], office.Lines.Select(line => (line.Kind, line.Amount.Amount, line.TicketId)));
    }
}
