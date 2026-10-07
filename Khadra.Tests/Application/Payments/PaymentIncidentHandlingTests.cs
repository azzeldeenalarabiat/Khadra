using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Payments.AdminPayments;
using Khadra.Application.Payments.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Payments;

/// <summary>
/// An administrator closing a capture incident (Wave 4, B1): once, with a note, audited in the same save, and without
/// moving any money — the money was dealt with at the provider, and this is the account of it.
/// </summary>
public sealed class PaymentIncidentHandlingTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly Id AdminId = Id.New();
    private const string BookingReference = "KH-INCIDENT1";

    private sealed class Context
    {
        public IPaymentIncidentRepository Incidents { get; } = Substitute.For<IPaymentIncidentRepository>();
        public IPaymentRepository Payments { get; } = Substitute.For<IPaymentRepository>();
        public IPaymentAdminReader Reader { get; } = Substitute.For<IPaymentAdminReader>();
        public IAuditTrail AuditTrail { get; } = Substitute.For<IAuditTrail>();
        public ICurrentActor Actor { get; } = Substitute.For<ICurrentActor>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public TestClock Clock { get; } = new(Now.AddHours(2));
        public List<AuditEntry> Audited { get; } = [];

        public Context()
        {
            Actor.UserId.Returns(AdminId);
            Actor.Role.Returns(UserRole.Admin);
            Actor.Name.Returns("Rania Haddad");
            Actor.CorrelationId.Returns("test");
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            AuditTrail.When(trail => trail.Record(Arg.Any<AuditEntry>()))
                .Do(call => Audited.Add(call.Arg<AuditEntry>()));
        }

        public (Payment Payment, PaymentIncident Incident) GivenIncident()
        {
            var payment = Payment.Open(Id.New(), Id.New(), Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now);
            payment.AttachProviderSession("sess_1", "https://provider.test/sess_1");
            Assert.True(payment.Apply(Money.Jod(18m), Now, Now, "cap_1").IsSuccess);
            var incident = PaymentIncident.Raise(
                PaymentIncidentKind.SecondCapture, payment.Id, Id.New(), "TestProvider", "cap_2",
                Money.Jod(18m), Money.Jod(18m), otherPaymentId: null, Now);

            Payments.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);
            Incidents.GetByIdAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);
            Reader.BookingLinkAsync(payment.BookingId, Arg.Any<CancellationToken>()).Returns(new PaymentBookingLink(
                payment.BookingId.Value, BookingReference, "Confirmed", Guid.NewGuid(), "Arabiat Car", payment.CustomerId.Value, "A customer"));
            return (payment, incident);
        }

        public MarkPaymentIncidentHandledHandler Handler() =>
            new(Incidents, Payments, Reader, new AdminActionRecorder(AuditTrail, Actor, Clock), UnitOfWork, Clock);
    }

    private static MarkPaymentIncidentHandledCommand Command(Payment payment, PaymentIncident incident, string? note = "Refunded at the provider, ticket 41.") =>
        new(payment.Id, incident.Id, AdminId, note);

    [Fact]
    public async Task Marking_an_incident_handled_records_who_when_and_how_with_its_audit_entry_in_one_save()
    {
        var context = new Context();
        var (payment, incident) = context.GivenIncident();

        var result = await context.Handler().Handle(
            Command(payment, incident, "  Refunded at the provider, ticket 41.  "), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.True(incident.IsHandled);
        Assert.Equal(AdminId, incident.HandledByAdminId);
        Assert.Equal(context.Clock.UtcNow, incident.HandledAt);
        Assert.Equal("Refunded at the provider, ticket 41.", incident.HandledNote);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var entry = Assert.Single(context.Audited);
        Assert.Same(AuditAction.PaymentIncidentHandled, entry.Action);
        Assert.Same(AuditEntityType.PaymentIncident, entry.EntityType);
        Assert.Equal(incident.Id, entry.EntityId);
        Assert.Equal(AdminId, entry.ActorUserId);
        // Labelled by the booking's reference, never by a person: the audit log can never be erased.
        Assert.Equal(BookingReference, entry.SubjectLabel);
        Assert.Null(entry.PreviousValue);
        Assert.Equal("SecondCapture", entry.NewValue);
        Assert.Equal("Refunded at the provider, ticket 41.", entry.Reason);

        // And no money moved: the payment is exactly as it was.
        Assert.Same(PaymentStatus.Applied, payment.Status);
        Assert.Empty(payment.Refunds);
    }

    /// <summary>An incident is reached through its own payment's page; named under another payment it does not exist.</summary>
    [Fact]
    public async Task An_incident_named_under_another_payment_is_not_found_and_nothing_is_written()
    {
        var context = new Context();
        var (_, incident) = context.GivenIncident();

        var result = await context.Handler().Handle(
            new MarkPaymentIncidentHandledCommand(Id.New(), incident.Id, AdminId, "note"), CancellationToken.None);

        Assert.Equal("payments.incident_not_found", result.Error.Code);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.False(incident.IsHandled);
        Assert.Empty(context.Audited);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_incident_is_not_found()
    {
        var context = new Context();
        var (payment, _) = context.GivenIncident();

        var result = await context.Handler().Handle(
            new MarkPaymentIncidentHandledCommand(payment.Id, Id.New(), AdminId, "note"), CancellationToken.None);

        Assert.Equal("payments.incident_not_found", result.Error.Code);
        Assert.Empty(context.Audited);
    }

    /// <summary>Once. The first administrator's account stands, and a second one is told so rather than overwriting it.</summary>
    [Fact]
    public async Task An_incident_already_handled_is_refused_and_keeps_its_first_account()
    {
        var context = new Context();
        var (payment, incident) = context.GivenIncident();
        var first = Id.New();
        Assert.True(incident.MarkHandled(first, "First account.", Now.AddHours(1)).IsSuccess);

        var result = await context.Handler().Handle(Command(payment, incident, "Second account."), CancellationToken.None);

        Assert.Equal("payments.incident_already_handled", result.Error.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Equal(first, incident.HandledByAdminId);
        Assert.Equal("First account.", incident.HandledNote);
        Assert.Empty(context.Audited);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Two administrators at once: both read it open, the row's concurrency token refuses the second save, and the
    /// second is told it was already handled — never a 500, and never a second account written over the first.
    /// </summary>
    [Fact]
    public async Task Two_administrators_at_once_the_second_is_told_it_was_already_handled()
    {
        var context = new Context();
        var (payment, incident) = context.GivenIncident();
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new ConcurrencyConflictException());

        var result = await context.Handler().Handle(Command(payment, incident), CancellationToken.None);

        Assert.Equal("payments.incident_already_handled", result.Error.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
    }

    /// <summary>A booking that no longer resolves does not stop the record: the payment's id labels it instead.</summary>
    [Fact]
    public async Task Without_a_booking_to_name_the_entry_is_labelled_by_the_payment()
    {
        var context = new Context();
        var (payment, incident) = context.GivenIncident();
        context.Reader.BookingLinkAsync(payment.BookingId, Arg.Any<CancellationToken>()).Returns((PaymentBookingLink?)null);

        var result = await context.Handler().Handle(Command(payment, incident), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(payment.Id.Value.ToString(), Assert.Single(context.Audited).SubjectLabel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_incident_is_not_closed_without_saying_how(string? note)
    {
        var result = new MarkPaymentIncidentHandledCommandValidator().Validate(
            new MarkPaymentIncidentHandledCommand(Id.New(), Id.New(), AdminId, note));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_note_is_at_most_its_column()
    {
        var validator = new MarkPaymentIncidentHandledCommandValidator();

        Assert.True(validator.Validate(new MarkPaymentIncidentHandledCommand(
            Id.New(), Id.New(), AdminId, new string('a', PaymentIncident.MaxNoteLength))).IsValid);
        Assert.False(validator.Validate(new MarkPaymentIncidentHandledCommand(
            Id.New(), Id.New(), AdminId, new string('a', PaymentIncident.MaxNoteLength + 1))).IsValid);
    }
}
