using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.FinancialDocuments.Issuance;
using Khadra.Application.FinancialDocuments.Queries;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.FinancialDocuments.VoidFinancialDocument;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Fleet;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Payments;
using Khadra.Domain.PlatformSettings;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Khadra.Tests.Support;

/// <summary>
/// Issuing financial documents with the real repositories, readers and unit of work over any database
/// (payments Phase 5): SQLite for the suite, a scratch PostgreSQL for the opt-in proofs. The settlement
/// pass's issuing step is run exactly as <c>BookingSettlementService</c> runs it — one query for the work,
/// then a context per document, holds in a context of their own.
/// </summary>
internal sealed class IssuanceHarness(DbContextOptions<KhadraDbContext> options)
{
    public static readonly DateTimeOffset Start = Build.Now;

    /// <summary>The clock every handler reads.</summary>
    public DateTimeOffset Now { get; set; } = Start.AddMinutes(1);

    /// <summary>The configured identity; null is "not configured".</summary>
    public DocumentIssuer? Issuer { get; set; } = DocumentFixtures.Issuer;

    public KhadraDbContext NewContext() => new(options);

    public TestDocumentSettings Settings() => new(Issuer);

    public static UnitOfWork UnitOfWork(KhadraDbContext context) => new(context, Substitute.For<IDomainEventDispatcher>());

    /// <summary>One settlement pass's issuing, exactly as <c>BookingSettlementService</c> runs it.</summary>
    public async Task<List<IssuanceOutcome>> PassAsync()
    {
        IReadOnlyList<IssuanceCandidate> work;
        await using (var context = NewContext())
        {
            work = await new ListFinancialDocumentWorkHandler(new FinancialDocumentCandidateReader(context), Settings(), new TestClock(Now))
                .Handle(new ListFinancialDocumentWorkQuery(), CancellationToken.None);
        }

        var outcomes = new List<IssuanceOutcome>();
        foreach (var candidate in work)
        {
            IssuanceOutcome outcome;
            await using (var context = NewContext())
            {
                outcome = await IssueHandler(context).Handle(
                    new IssueFinancialDocumentCommand(candidate.Type, candidate.SubjectId, candidate.BookingId),
                    CancellationToken.None);
            }

            outcomes.Add(outcome);
            if (outcome.Hold is { } hold)
            {
                await using var context = NewContext();
                await new RecordFinancialDocumentHoldHandler(
                        new FinancialDocumentIssuanceHoldRepository(context),
                        Settings(),
                        UnitOfWork(context),
                        new TestClock(Now),
                        new RecordingLogger<RecordFinancialDocumentHoldHandler>())
                    .Handle(new RecordFinancialDocumentHoldCommand(candidate.Type, candidate.SubjectId, candidate.BookingId, hold.Reason, hold.Error), CancellationToken.None);
            }
        }

        return outcomes;
    }

    public async Task<CSharpFunctionalExtensions.Result<VoidedFinancialDocumentDto, Error>> VoidAsync(Id documentId, string reason)
    {
        await using var context = NewContext();
        var documents = new FinancialDocumentRepository(context);
        var actor = Substitute.For<ICurrentActor>();
        var admin = Id.New();
        actor.UserId.Returns(admin);
        actor.Role.Returns(UserRole.Admin);
        actor.Name.Returns("Test Admin");
        actor.CorrelationId.Returns("test");
        var handler = new VoidFinancialDocumentHandler(
            documents,
            Preparation(context, documents),
            new FinancialDocumentIssuing(new FinancialDocumentSeriesCounter(context), documents, DocumentFixtures.Amman),
            new AdminActionRecorder(new AuditTrail(context), actor, new TestClock(Now)),
            UnitOfWork(context),
            new TestClock(Now),
            new RecordingLogger<VoidFinancialDocumentHandler>());
        return await handler.Handle(new VoidFinancialDocumentCommand(documentId, admin, reason), CancellationToken.None);
    }

    /// <summary>A customer, an office with a city and an address, and a car of a type — saved, as documents read them.</summary>
    /// <param name="unique">
    /// Makes every uniquely indexed value — the customer's email and phone, the office's registration, the
    /// car's plate — unique when one database holds several sets of parties.
    /// </param>
    public async Task<(User Customer, Dealer Dealer, Vehicle Vehicle)> PartiesAsync(string unique = "")
    {
        var digits = new string([.. unique.Where(char.IsAsciiDigit)]);
        var customer = Build.Customer(
            email: $"rana{unique}@example.jo",
            phone: digits.Length == 0 ? "0791234567" : "079" + digits.PadRight(7, '0')[..7]);
        var dealer = Build.ApprovedDealer(commercialRegistration: digits.Length == 0 ? "123456" : digits[..Math.Min(10, digits.Length)]);
        var city = City.Create("Amman" + unique, "عمّان" + unique, 1, Start).Value;
        var carType = CarType.Create("Sedan" + unique, "سيدان" + unique, 1, Start).Value;
        if (dealer.UpdateProfile(dealer.BusinessName, dealer.Location, dealer.OperatingHours, city.Id, DealerAddress.Create("Abdoun", "Street 12").Value).IsFailure)
            throw new InvalidOperationException("The test office could not be placed.");
        var vehicle = Build.Vehicle(dealer.Id, carTypeId: carType.Id, plateNumber: digits.Length == 0 ? "12-34567" : $"12-{digits[..Math.Min(5, digits.Length)].PadLeft(5, '0')}");
        await using var context = NewContext();
        context.Users.Add(customer);
        context.Dealers.Add(dealer);
        context.Cities.Add(city);
        context.CarTypes.Add(carType);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        return (customer, dealer, vehicle);
    }

    /// <summary>A booking paid the way production pays, with real parties saved beside it.</summary>
    public async Task<(Booking Booking, Payment Payment)> PaidAsync(string provider, bool inFull = false, decimal fee = 0m, string unique = "")
    {
        var (customer, dealer, vehicle) = await PartiesAsync(unique);
        var booking = Build.Booking(Now, customerId: customer.Id, dealerId: dealer.Id, vehicleId: vehicle.Id);
        if (booking.Approve(Id.New(), Now).IsFailure)
            throw new InvalidOperationException("The test booking could not be approved.");
        var part = inFull ? booking.Pricing.TotalPrice : booking.Pricing.DepositAmount;
        var charged = Money.Create(part.Amount + fee, part.CurrencyCode);
        var payment = Payment.Open(
            booking.Id, booking.CustomerId, charged, provider, Now.AddMinutes(30), Now,
            inFull ? PaymentPurpose.FullPayment : PaymentPurpose.Deposit, Money.Jod(fee), true);
        if (payment.AttachProviderSession("sess_" + booking.Reference.Value, "https://provider.test/checkout").IsFailure
            || payment.Apply(Money.Create(charged.Amount, charged.CurrencyCode), Now, Now).IsFailure
            || booking.ConfirmPayment(payment.Id, payment.AppliedToBooking, Now).IsFailure)
        {
            throw new InvalidOperationException("The test payment could not be taken.");
        }

        booking.ClearDomainEvents();
        payment.ClearDomainEvents();
        await SaveAsync(booking, payment);
        return (booking, payment);
    }

    public async Task SaveAsync(Booking booking, Payment payment)
    {
        await using var context = NewContext();
        context.Bookings.Add(booking);
        context.Payments.Add(payment);
        await context.SaveChangesAsync();
    }

    public async Task ChangeAsync(Func<KhadraDbContext, Task> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var context = NewContext();
        await change(context);
        await context.SaveChangesAsync();
    }

    public async Task<List<FinancialDocument>> DocumentsAsync()
    {
        await using var context = NewContext();
        return await context.FinancialDocuments.AsNoTracking().ToListAsync();
    }

    public async Task<List<FinancialDocumentIssuanceHold>> HoldsAsync()
    {
        await using var context = NewContext();
        return await context.FinancialDocumentIssuanceHolds.AsNoTracking().ToListAsync();
    }

    private IssueFinancialDocumentHandler IssueHandler(KhadraDbContext context)
    {
        var documents = new FinancialDocumentRepository(context);
        return new IssueFinancialDocumentHandler(
            Preparation(context, documents),
            new FinancialDocumentIssuing(new FinancialDocumentSeriesCounter(context), documents, DocumentFixtures.Amman),
            documents,
            new FinancialDocumentIssuanceHoldRepository(context),
            UnitOfWork(context),
            new TestClock(Now),
            new RecordingLogger<IssueFinancialDocumentHandler>());
    }

    private DocumentPreparation Preparation(KhadraDbContext context, FinancialDocumentRepository documents) =>
        new(
            new FinancialDocumentFactsReader(new BookingRepository(context), new PaymentRepository(context), new DisputeTicketRepository(context), context),
            documents,
            Settings(),
            DocumentFixtures.Composer());
}

/// <summary>The issuing settings a test controls.</summary>
internal sealed class TestDocumentSettings(DocumentIssuer? issuer) : IFinancialDocumentSettings
{
    public DocumentIssuer? Issuer => issuer;
    public int MaxDocumentsPerPass => 200;
    public TimeSpan RetryInitialDelay => TimeSpan.FromMinutes(1);
    public TimeSpan RetryMaxDelay => TimeSpan.FromHours(1);
    public TimeSpan LateCommitMargin => TimeSpan.FromMinutes(10);
}
