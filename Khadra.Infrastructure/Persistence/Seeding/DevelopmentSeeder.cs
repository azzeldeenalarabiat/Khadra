using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Disputes;
using Khadra.Domain.Fleet;
using Khadra.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Khadra.Infrastructure.Persistence.Seeding;

/// <summary>
/// Fills an empty development database with a platform that looks like it has been running.
///
/// Everything is built through the real domain factories and real state transitions, never by
/// inserting rows. That is deliberate: a seeder that writes tables directly can produce states the
/// domain forbids, and then the dashboard is verified against data the application could never have
/// created. Doing it the long way also means this file fails loudly the day an invariant changes.
///
/// The clock is wound backwards: bookings are created at historical instants and walked forward
/// through their lifecycle, so the fourteen-day trend, the "today" figure and the SLA countdowns all
/// have something real to measure.
/// </summary>
internal sealed partial class DevelopmentSeeder(
    KhadraDbContext context,
    IPasswordHasher passwordHasher,
    IBusinessRulesProvider businessRules,
    IClock clock,
    ILogger<DevelopmentSeeder> logger)
{
    public const string AdminEmail = "admin@khadra.jo";
    public const string SeedPassword = "Khadra!2026";

    private readonly Random _random = new(20260903);

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Guarded on dealers, not users: this seeder builds a MARKETPLACE, and a development
        // database very often already holds a handful of accounts from manual auth testing. Refusing
        // to seed because one of those exists would leave the console permanently empty, while
        // guarding on the thing actually being created still makes the seed strictly once-only.
        if (await context.Dealers.AnyAsync(cancellationToken))
        {
            LogSkipped(logger);
            return;
        }

        var now = clock.UtcNow;
        var rules = await businessRules.GetAsync(cancellationToken);
        // One hash, reused for every seeded account. BCrypt at the configured work factor takes about
        // a quarter of a second, and hashing three hundred identical dev passwords would turn seeding
        // into a coffee break for no benefit.
        var sharedHash = passwordHasher.Hash(SeedPassword);

        var admins = SeedAdmins(sharedHash, now);
        var customers = SeedCustomers(sharedHash, now);
        var (dealers, dealerOwners) = SeedDealers(sharedHash, now, TimeSpan.FromHours(rules.AdminSlaHours), admins[0].Id);

        context.Users.AddRange(admins);
        context.Users.AddRange(customers);
        context.Users.AddRange(dealerOwners);
        context.Dealers.AddRange(dealers);

        var tradingDealers = dealers.Where(dealer => dealer.CanTrade).ToList();
        var bookings = SeedBookings(tradingDealers, customers, rules, now);
        context.Bookings.AddRange(bookings);

        var tickets = SeedDisputes(bookings, TimeSpan.FromHours(rules.AdminSlaHours), now, admins[0].Id);
        context.DisputeTickets.AddRange(tickets);

        context.AuditEntries.AddRange(SeedAuditTrail(admins, dealers, tickets, now));

        // Domain events raised while building are not meaningful for seed data: there is nobody to
        // email about a dealer approved in a fabricated past.
        foreach (var aggregate in context.ChangeTracker.Entries<AggregateRoot>())
            aggregate.Entity.ClearDomainEvents();

        await context.SaveChangesAsync(cancellationToken);

        LogComplete(logger, dealers.Count, customers.Count, bookings.Count, tickets.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development seed skipped: the database already holds dealers.")]
    private static partial void LogSkipped(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Development seed complete: {Dealers} dealers, {Customers} customers, {Bookings} bookings, {Tickets} disputes.")]
    private static partial void LogComplete(ILogger logger, int dealers, int customers, int bookings, int tickets);

    private static List<User> SeedAdmins(string passwordHash, DateTimeOffset now) =>
    [
        Admin(AdminEmail, "0790000001", "Rania Haddad", passwordHash, now.AddYears(-1)),
        Admin("omar@khadra.jo", "0790000002", "Omar Deeb", passwordHash, now.AddMonths(-8)),
        Admin("yousef@khadra.jo", "0790000003", "Yousef Barakat", passwordHash, now.AddMonths(-6))
    ];

    private static User Admin(string email, string phone, string name, string passwordHash, DateTimeOffset createdAt) =>
        User.CreateAdmin(
            EmailAddress.Create(email).Value,
            PhoneNumber.Create(phone).Value,
            PersonName.Create(name).Value,
            PasswordHash.FromHash(passwordHash),
            createdAt);

    private List<User> SeedCustomers(string passwordHash, DateTimeOffset now)
    {
        var given = new[]
        {
            "Layla", "Sami", "Omar", "Huda", "Rami", "Nour", "Aisha", "Khalid", "Dana", "Ziad",
            "Maha", "Tariq", "Salma", "Bashar", "Rana", "Fadi", "Lina", "Hakim", "Yara", "Nabil"
        };
        var family = new[]
        {
            "Odeh", "Farah", "Nasser", "Sabbagh", "Zayed", "Halabi", "Kamal", "Amr", "Qasem", "Karam",
            "Khoury", "Haddad", "Mansour", "Sweiss", "Barakat", "Deeb", "Jaber", "Sharif", "Awad", "Rifai"
        };

        var customers = new List<User>();
        for (var index = 0; index < 200; index++)
        {
            var name = $"{given[index % given.Length]} {family[(index / given.Length + index) % family.Length]}";
            // Spread joining dates over two years so the platform does not look like it opened today.
            var joined = now.AddDays(-_random.Next(1, 730)).AddHours(-_random.Next(0, 24));
            var customer = User.RegisterCustomer(
                EmailAddress.Create($"customer{index + 1}@example.jo").Value,
                PhoneNumber.Create($"078{(1000000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture)}").Value,
                PersonName.Create(name).Value,
                PasswordHash.FromHash(passwordHash),
                joined);

            // Most customers have verified their email; a minority are still pending, and a few have
            // been suspended. The dashboard buckets have to add up to the total, so these are
            // deliberately exclusive.
            var roll = _random.Next(100);
            if (roll < 85)
                customer.VerifyEmail(joined.AddMinutes(12));
            else if (roll >= 97)
            {
                customer.VerifyEmail(joined.AddMinutes(12));
                customer.Suspend("Repeated no-shows.", joined.AddDays(30));
            }

            customers.Add(customer);
        }

        return customers;
    }

    private static (List<Dealer> Dealers, List<User> Owners) SeedDealers(
        string passwordHash,
        DateTimeOffset now,
        TimeSpan reviewSla,
        Id adminId)
    {
        // (business name, city, latitude, longitude, outcome). The outcomes are chosen so every state
        // the dashboard can show is present, including two applications already past their SLA.
        var definitions = new (string Name, double Lat, double Lng, string Outcome)[]
        {
            ("Al-Nadeem Rentals", 31.9539, 35.9106, "approved"),
            ("Aqaba Coast Cars", 29.5321, 35.0063, "approved"),
            ("Petra Wheels", 30.3285, 35.4444, "approved"),
            ("Amman Central Motors", 31.9450, 35.9280, "approved"),
            ("Zarqa Auto Lease", 32.0728, 36.0880, "approved"),
            ("Irbid City Cars", 32.5556, 35.8500, "approved"),
            ("Dead Sea Drive", 31.5590, 35.4732, "suspended"),
            ("Jerash Rentals", 32.2811, 35.8990, "overdue"),
            ("Wadi Rum Motors", 29.5765, 35.4200, "overdue"),
            ("Madaba Car Hire", 31.7160, 35.7930, "warning"),
            ("Salt Vehicle Rental", 32.0392, 35.7272, "pending"),
            ("Ajloun Auto", 32.3325, 35.7519, "clarification"),
            ("Karak Rentals", 31.1850, 35.7047, "rejected"),
            ("Mafraq Motors", 32.3430, 36.2080, "approved")
        };

        var dealers = new List<Dealer>();
        var owners = new List<User>();

        for (var index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            var registeredAt = definition.Outcome switch
            {
                // Past the SLA: submitted well over the window ago and still untouched.
                "overdue" => now.AddHours(-(reviewSla.TotalHours + 8 + index)),
                // Inside the window but close enough to it to be flagged.
                "warning" => now.AddHours(-(reviewSla.TotalHours * 0.9)),
                "pending" => now.AddHours(-3),
                _ => now.AddDays(-(90 + (index * 11)))
            };

            var owner = User.RegisterDealerOwner(
                EmailAddress.Create($"owner{index + 1}@khadra-dealers.jo").Value,
                PhoneNumber.Create($"079{(2000000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture)}").Value,
                PersonName.Create($"{definition.Name.Split(' ')[0]} Owner").Value,
                PasswordHash.FromHash(passwordHash),
                registeredAt);
            owner.VerifyEmail(registeredAt.AddMinutes(5));
            owners.Add(owner);

            var dealer = Dealer.Register(
                owner.Id,
                BusinessName.Create(definition.Name).Value,
                CommercialRegistrationNumber.Create((89000 + (index * 137)).ToString(System.Globalization.CultureInfo.InvariantCulture)).Value,
                GeoPoint.Create(definition.Lat, definition.Lng).Value,
                OperatingHours.Uniform(new TimeOnly(8, 0), new TimeOnly(20, 0)).Value,
                registeredAt,
                reviewSla);

            foreach (var type in DealerDocumentType.Required)
                dealer.AttachDocument(type, $"dealers/{dealer.Id.Value}/{type.Name}.pdf", registeredAt.AddMinutes(20));

            switch (definition.Outcome)
            {
                case "approved":
                    dealer.Approve(adminId, registeredAt.AddHours(6));
                    dealer.EnableDelivery(30m, registeredAt.AddHours(7));
                    break;
                case "suspended":
                    dealer.Approve(adminId, registeredAt.AddHours(6));
                    dealer.Suspend(adminId, "Unresolved disputes on three consecutive bookings.", now.AddDays(-4));
                    break;
                case "clarification":
                    dealer.RequestClarification(adminId, "The commercial registration scan is unreadable.", registeredAt.AddHours(9));
                    break;
                case "rejected":
                    dealer.Reject(adminId, "The commercial registration has expired.", registeredAt.AddHours(11));
                    break;
                default:
                    // "pending", "warning" and "overdue" are all left awaiting a first decision; only
                    // their submission time differs, which is what puts them in different SLA states.
                    break;
            }

            dealers.Add(dealer);
        }

        return (dealers, owners);
    }

    private List<Booking> SeedBookings(
        List<Dealer> tradingDealers,
        List<User> customers,
        BusinessRules rules,
        DateTimeOffset now)
    {
        var bookings = new List<Booking>();

        // Twenty-eight days: the chart shows fourteen and compares them against the fourteen before,
        // so the change figure needs both halves. Volume rises across the window so the comparison is
        // positive and visibly so.
        for (var daysAgo = 27; daysAgo >= 0; daysAgo--)
        {
            var perDay = 8 + ((27 - daysAgo) / 3) + _random.Next(0, 5);
            for (var index = 0; index < perDay; index++)
            {
                var createdAt = now.AddDays(-daysAgo)
                    .AddHours(-_random.Next(0, 20))
                    .AddMinutes(-_random.Next(0, 60));
                if (createdAt >= now)
                    continue;

                // Terms are built per booking, not once and shared. That is what the real flow does
                // -- each booking freezes its own snapshot of the rules -- and it is also required:
                // one value-object instance cannot be owned by three hundred bookings at once.
                var booking = CreateBooking(tradingDealers, customers, BuildTerms(rules), rules, createdAt);
                if (booking is not null)
                    bookings.Add(booking);
            }
        }

        return bookings;
    }

    private Booking? CreateBooking(
        List<Dealer> tradingDealers,
        List<User> customers,
        BookingTerms terms,
        BusinessRules rules,
        DateTimeOffset createdAt)
    {
        var dealer = tradingDealers[_random.Next(tradingDealers.Count)];
        var customer = customers[_random.Next(customers.Count)];

        var days = _random.Next(2, 9);
        // The period must start after the booking was made; a couple of days of lead time is typical.
        var start = createdAt.AddDays(_random.Next(1, 4));
        var period = DateRange.Create(start, start.AddDays(days));
        if (period.IsFailure)
            return null;

        var dailyRate = Money.Jod(25m + (_random.Next(0, 16) * 5m));
        var delivery = _random.Next(100) < 35;
        var pricing = BookingPricing.Calculate(
            dailyRate,
            period.Value.WholeDays,
            delivery ? Money.Jod(rules.DeliveryFee.Amount) : Money.Jod(0m),
            Percentage.FromValidated(rules.DepositPercent),
            Money.Jod(150m),
            MileagePolicy.Limited(200, Money.Jod(0.15m)).Value,
            FuelPolicy.FullToFull);
        if (pricing.IsFailure)
            return null;

        var booking = Booking.Create(
            customer.Id,
            dealer.Id,
            Id.New(),
            period.Value,
            delivery ? PickupMethod.Delivery : PickupMethod.SelfPickup,
            delivery ? dealer.Location : null,
            pricing.Value,
            terms,
            PaymentOption.DepositOnly,
            createdAt);

        return booking.IsFailure ? null : Advance(booking.Value, rules, createdAt);
    }

    /// <summary>
    /// Walks a booking forward through its real lifecycle so the status mix looks like a live
    /// platform. Every transition is the domain method, at a plausible instant, which means the seeded
    /// data satisfies the same invariants a real booking would.
    /// </summary>
    private Booking Advance(Booking booking, BusinessRules rules, DateTimeOffset createdAt)
    {
        var now = clock.UtcNow;
        var roll = _random.Next(100);

        // Abandoned checkout: never paid, and past the payment window.
        if (roll < 6)
        {
            var deadline = createdAt.AddMinutes(rules.PaymentWindowMinutes + 5);
            if (deadline <= now)
                booking.ExpireUnpaid(deadline);
            return booking;
        }

        // Still inside the payment window: left as it is, holding the vehicle.
        if (roll < 10)
            return booking;

        booking.ConfirmDepositPaid(Id.New(), createdAt.AddMinutes(4));

        // Waiting on the dealer.
        if (roll < 22)
            return booking;

        if (roll < 27)
        {
            booking.Reject(Id.New(), "The vehicle is already committed for those dates.", createdAt.AddHours(3));
            return booking;
        }

        var approvedAt = createdAt.AddHours(2);
        booking.Approve(Id.New(), approvedAt);

        if (roll < 33)
        {
            booking.Cancel(BookingParty.Customer, booking.CustomerId, "Travel plans changed.", approvedAt.AddHours(5));
            return booking;
        }

        var pickupDue = booking.Period.Start;
        if (pickupDue > now)
            return booking; // Approved and upcoming.

        if (roll < 38)
        {
            var noShowAt = pickupDue.Add(TimeSpan.FromHours(rules.NoShowTimeoutHours)).AddMinutes(10);
            if (noShowAt <= now)
                booking.MarkNoShow(noShowAt);
            return booking;
        }

        booking.RecordPickup(BookingParty.Dealer, Id.New(), pickupDue.AddMinutes(20), odometerKm: 42_000, fuelLevel: 1m);

        var returnDue = booking.Period.End;
        if (returnDue > now)
            return booking; // Out on hire right now.

        booking.RecordReturn(BookingParty.Dealer, Id.New(), returnDue.AddMinutes(15), odometerKm: 42_650, fuelLevel: 1m);

        // A returned booking settles itself once the quiet period passes with no dispute. The ones
        // left un-settled here are what the dispute seeding attaches to.
        var settleAt = returnDue.AddHours(rules.PostReturnSettlementHours).AddMinutes(5);
        if (settleAt <= now && roll < 92)
            booking.Settle(settleAt, hasOpenDispute: false);

        return booking;
    }

    private static BookingTerms BuildTerms(BusinessRules rules) =>
        BookingTerms.Create(
            Percentage.FromValidated(rules.DepositPercent),
            Percentage.FromValidated(rules.CommissionPercent),
            TimeSpan.FromMinutes(rules.FreeCancellationWindowMinutes),
            TimeSpan.FromHours(rules.NoShowTimeoutHours),
            TimeSpan.FromMinutes(rules.PaymentWindowMinutes),
            TimeSpan.FromHours(rules.PostReturnSettlementHours),
            Percentage.FromValidated(rules.CustomerCancellationPenaltyPercent),
            Percentage.FromValidated(rules.DealerNonDeliveryPenaltyMinPercent),
            Percentage.FromValidated(rules.DealerNonDeliveryPenaltyMaxPercent),
            rulesVersion: 1).Value;

    private static List<DisputeTicket> SeedDisputes(List<Booking> bookings, TimeSpan sla, DateTimeOffset now, Id adminId)
    {
        var reasons = new[]
        {
            "Customer claims the vehicle was returned undamaged; the dealer applied a penalty for a scratch on the rear bumper.",
            "Deposit forfeited after a no-show the customer says was caused by a delayed flight.",
            "The vehicle was not delivered to the agreed address at the agreed time.",
            "Late return by four hours; the dealer charged a full extra day.",
            "Fuel level on return is disputed by both parties.",
            "Cancellation fee applied inside what the customer believes was the free window.",
            "Vehicle cleanliness at handover was not as advertised."
        };

        // Only bookings that actually finished can be disputed, which is the domain rule the ticket
        // relies on and the reason the seeder picks from this set rather than from all bookings.
        //
        // Completed is excluded to match Booking.CanBeDisputed: reaching Completed IS the settlement
        // window having elapsed, so a ticket on one would be a record the domain would now refuse to
        // create. The seeder must not manufacture data the rules forbid.
        var candidates = bookings
            .Where(booking => booking.Status == BookingStatus.Returned ||
                              booking.Status == BookingStatus.NoShow ||
                              booking.Status == BookingStatus.Cancelled)
            .OrderByDescending(booking => booking.CreatedAt)
            .Take(40)
            .ToList();

        var tickets = new List<DisputeTicket>();
        // Two breached tickets, several live inside the window, and a handful already resolved: the
        // dashboard should show a queue with real overdue items at the top of it.
        var openedOffsets = new[]
        {
            sla.Add(TimeSpan.FromHours(13)),   // overdue
            sla.Add(TimeSpan.FromHours(6)),    // overdue
            TimeSpan.FromHours(44),            // approaching
            TimeSpan.FromHours(41),            // approaching
            TimeSpan.FromHours(19),
            TimeSpan.FromMinutes(38),
            TimeSpan.FromHours(3)
        };

        for (var index = 0; index < openedOffsets.Length && index < candidates.Count; index++)
        {
            var booking = candidates[index];
            var openedAt = now - openedOffsets[index];
            var ticket = DisputeTicket.Open(
                booking.Id,
                booking.CustomerId,
                BookingParty.Customer,
                reasons[index % reasons.Length],
                sla,
                openedAt,
                [$"disputes/{booking.Reference.Value}/evidence-1.jpg"]);

            if (ticket.IsFailure)
                continue;

            // A couple have been picked up by an admin but not yet decided.
            if (index is 2 or 4)
                ticket.Value.AssignToAdmin(Id.New());

            tickets.Add(ticket.Value);
        }

        // Two tickets that have already been decided, so the "resolved recently" figure and the
        // median-resolution story on the console have something behind them.
        for (var index = openedOffsets.Length; index < openedOffsets.Length + 2 && index < candidates.Count; index++)
        {
            var booking = candidates[index];
            var openedAt = now.AddDays(-(index - openedOffsets.Length + 3));
            var ticket = DisputeTicket.Open(
                booking.Id,
                booking.CustomerId,
                BookingParty.Customer,
                reasons[index % reasons.Length],
                sla,
                openedAt);
            if (ticket.IsFailure)
                continue;

            ticket.Value.AssignToAdmin(adminId);

            // The deposit the platform is holding, copied rather than referenced: that Money instance
            // already belongs to the booking, and one value object cannot be owned by two aggregates.
            var held = Money.Create(booking.Pricing.DepositAmount.Amount, booking.Pricing.DepositAmount.CurrencyCode);
            var retained = Money.Create(decimal.Round(held.Amount / 2m, 3), held.CurrencyCode);
            var refund = Money.Create(held.Amount - retained.Amount, held.CurrencyCode);
            var disposition = DepositDisposition.Create(
                held, refund, retained, Money.ZeroIn(held.CurrencyCode));
            if (disposition.IsFailure)
                continue;

            var resolution = DisputeResolution.Create(
                disposition.Value,
                dealerCharge: null,
                assessedPenalty: booking.Penalty,
                note: "Evidence from both sides reviewed; the penalty was applied at half the assessed amount.",
                resolvedByAdminId: adminId,
                resolvedAt: openedAt.AddHours(19));
            if (resolution.IsSuccess)
                ticket.Value.Resolve(resolution.Value);

            tickets.Add(ticket.Value);
        }

        return tickets;
    }

    private static List<AuditEntry> SeedAuditTrail(
        List<User> admins,
        List<Dealer> dealers,
        List<DisputeTicket> tickets,
        DateTimeOffset now)
    {
        var rania = admins[0];
        var omar = admins[1];
        var entries = new List<AuditEntry>();

        var approved = dealers.Where(dealer => dealer.VerificationStatus == DealerVerificationStatus.Approved).ToList();
        if (approved.Count > 0)
        {
            entries.Add(AuditEntry.By(
                rania.Id, rania.Name.Value, UserRole.Admin,
                AuditAction.DealerApproved, AuditEntityType.Dealer, approved[0].Id,
                approved[0].BusinessName.Value, now.AddMinutes(-9),
                previousValue: "PendingReview", newValue: "Approved",
                reason: "Documents verified."));
        }

        if (tickets.Count > 0)
        {
            entries.Add(AuditEntry.BySystem(
                AuditAction.DisputeOpened, AuditEntityType.Dispute, tickets[^1].Id,
                "Vehicle not delivered", now.AddMinutes(-38)));
        }

        var suspendedDealer = dealers.FirstOrDefault(dealer => dealer.IsSuspended);
        if (suspendedDealer is not null)
        {
            entries.Add(AuditEntry.By(
                omar.Id, omar.Name.Value, UserRole.Admin,
                AuditAction.DealerSuspended, AuditEntityType.Dealer, suspendedDealer.Id,
                suspendedDealer.BusinessName.Value, now.AddHours(-2),
                previousValue: "Trading", newValue: "Suspended",
                reason: "Unresolved disputes on three consecutive bookings."));
        }

        entries.Add(AuditEntry.By(
            rania.Id, rania.Name.Value, UserRole.Admin,
            AuditAction.BusinessRuleChanged, AuditEntityType.Setting, null,
            "commission_rate", now.AddHours(-5),
            previousValue: "18%", newValue: "20%", reason: "Board decision, Q3."));

        entries.Add(AuditEntry.By(
            omar.Id, omar.Name.Value, UserRole.Admin,
            AuditAction.CustomerSuspended, AuditEntityType.Customer, null,
            "Customer #882", now.AddHours(-7),
            previousValue: "Active", newValue: "Suspended", reason: "Two no-shows in thirty days."));

        if (tickets.Count > 1)
        {
            entries.Add(AuditEntry.By(
                rania.Id, rania.Name.Value, UserRole.Admin,
                AuditAction.DisputeResolved, AuditEntityType.Dispute, tickets[1].Id,
                "Damage penalty disputed", now.AddDays(-1),
                previousValue: "UnderReview", newValue: "Resolved", reason: "Partial penalty applied."));
        }

        entries.Add(AuditEntry.By(
            rania.Id, rania.Name.Value, UserRole.Admin,
            AuditAction.AdminInvited, AuditEntityType.AdminUser, admins[2].Id,
            admins[2].Name.Value, now.AddDays(-1).AddHours(-3),
            newValue: "Admin"));

        return entries;
    }
}
