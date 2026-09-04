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
    IDocumentStorage storage,
    ILogger<DevelopmentSeeder> logger)
{
    public const string AdminEmail = "admin@khadra.jo";
    public const string SeedPassword = "Khadra!2026";

    // Car types are a lookup table that does not exist yet, so every seeded car points at the same
    // placeholder id -- the one the console's add-car form also sends. It resolves to nothing today;
    // when the lookup ships, this becomes a real row rather than a new concept.
    private static readonly Id SeedCarTypeId =
        Id.From(Guid.Parse("01a06675-0000-7000-8000-000000000001"));

    private readonly Random _random = new(20260903);

    // The smallest structurally valid PDF: one blank page. A dealer's licence papers have to open
    // when an Admin clicks "Open secure preview" -- the review screen's whole purpose is looking at
    // them -- and a blank page is an honest stand-in where an invented scan would not be.
    private static readonly byte[] PlaceholderPdf = System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n" +
        "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
        "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>endobj\n" +
        "trailer<</Root 1 0 R>>\n" +
        "%%EOF\n");

    /// <summary>
    /// A storage key the storage layer will actually accept.
    ///
    /// LocalDocumentStorage.KeyPattern is case-sensitive (`[0-9a-z-]+` for the stem), so the earlier
    /// "{DealerId}/CommercialRegistration.pdf" matched nothing and ResolveWithinRoot THREW: every
    /// "Open secure preview" on every seeded application answered 500. Lowercased and hyphenated,
    /// the key is valid and the file below is written at it.
    /// </summary>
    internal static string DocumentKey(Id dealerId, DealerDocumentType type) =>
        $"dealers/{dealerId.Value}/{Slug(type.Name)}.pdf";

    /// <summary>"CommercialRegistration" -> "commercial-registration".</summary>
    private static string Slug(string name) =>
        string.Concat(name.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? $"-{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));

    // A valid 8x8 JPEG, plain grey. Enough for an <img> to load; not pretending to be a car.
    private static readonly byte[] PlaceholderJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAAIAAgBAREA/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oACAEBAAA/APn+iiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiv/Z");

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

        // A fleet for every dealer that got through review, INCLUDING the suspended one: it was
        // approved and trading before it was suspended, so an empty fleet there would be a story that
        // never happened. Dealers still pending, rejected or in clarification have none, because
        // RequireApprovedDealer would have refused them every car.
        var fleets = SeedFleets(dealers, now);
        context.Vehicles.AddRange(fleets.SelectMany(fleet => fleet.Value));

        var tradingDealers = dealers.Where(dealer => dealer.CanTrade).ToList();
        var bookings = SeedBookings(tradingDealers, fleets, customers, rules, now);
        context.Bookings.AddRange(bookings);

        var tickets = SeedDisputes(bookings, TimeSpan.FromHours(rules.AdminSlaHours), now, admins[0].Id);
        context.DisputeTickets.AddRange(tickets);

        context.AuditEntries.AddRange(SeedAuditTrail(admins, dealers, tickets, now));

        // Domain events raised while building are not meaningful for seed data: there is nobody to
        // email about a dealer approved in a fabricated past.
        foreach (var aggregate in context.ChangeTracker.Entries<AggregateRoot>())
            aggregate.Entity.ClearDomainEvents();

        await context.SaveChangesAsync(cancellationToken);

        // The photo every seeded car carries has to exist on disk, or the console shows a broken
        // image where a customer would see the car. A flat placeholder, written at the exact key the
        // listing quotes, through the same storage the real upload flow uses.
        foreach (var vehicle in fleets.SelectMany(fleet => fleet.Value))
        {
            foreach (var image in vehicle.Images)
            {
                using var bytes = new MemoryStream(PlaceholderJpeg);
                await storage.SaveAtAsync(image.StorageKey, "image/jpeg", bytes, cancellationToken);
            }
        }

        // Same reasoning for the licence papers behind every dealer application: the Admin's review
        // screen exists to open these, so they have to be there to open.
        foreach (var document in dealers.SelectMany(dealer => dealer.Documents))
        {
            using var bytes = new MemoryStream(PlaceholderPdf);
            await storage.SaveAtAsync(document.StorageKey, "application/pdf", bytes, cancellationToken);
        }

        var vehicleCount = fleets.Sum(fleet => fleet.Value.Count);
        LogComplete(logger, dealers.Count, customers.Count, vehicleCount, bookings.Count, tickets.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development seed skipped: the database already holds dealers.")]
    private static partial void LogSkipped(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Development seed complete: {Dealers} dealers, {Customers} customers, {Vehicles} vehicles, {Bookings} bookings, {Tickets} disputes.")]
    private static partial void LogComplete(
        ILogger logger,
        int dealers,
        int customers,
        int vehicles,
        int bookings,
        int tickets);

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
                dealer.AttachDocument(type, DocumentKey(dealer.Id, type), registeredAt.AddMinutes(20));

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

    // Models that are actually common on Jordanian rental lots, with a plausible daily rate band in
    // JOD. Rate drives the booking price, so these numbers decide what the whole platform's revenue
    // figures look like.
    private static readonly (string Make, string Model, int Seats, decimal Low, decimal High)[] CarCatalogue =
    [
        ("Toyota", "Corolla", 5, 28m, 38m),
        ("Toyota", "Camry", 5, 40m, 55m),
        ("Toyota", "RAV4", 5, 55m, 75m),
        ("Hyundai", "Elantra", 5, 26m, 36m),
        ("Hyundai", "Tucson", 5, 50m, 68m),
        ("Kia", "Rio", 5, 22m, 30m),
        ("Kia", "Sportage", 5, 48m, 65m),
        ("Nissan", "Sunny", 5, 22m, 30m),
        ("Nissan", "X-Trail", 7, 60m, 80m),
        ("Mitsubishi", "Attrage", 5, 24m, 32m),
        ("Chevrolet", "Malibu", 5, 38m, 52m),
        ("Mercedes-Benz", "E-Class", 5, 95m, 130m),
        ("BMW", "5 Series", 5, 95m, 135m),
        ("Hyundai", "Staria", 9, 85m, 110m),
    ];

    private static readonly string[] CarColours =
        ["White", "Silver", "Black", "Grey", "Dark blue", "Beige"];

    /// <summary>
    /// Gives every approved dealer a real fleet, and hands back the cars per dealer so bookings can be
    /// made against actual vehicles.
    ///
    /// Before this existed the seeder invented a fresh vehicle id per booking, so all 393 bookings
    /// pointed at cars that had never existed: the dealer list showed every fleet as empty, and any
    /// screen that named the car in a booking or a dispute had nothing to name.
    ///
    /// The status mix is the one a real lot has -- mostly published, a few taken down, the odd one in
    /// the garage and a couple never finished -- and every car is put there through the same domain
    /// methods a dealer's own clicks would call.
    /// </summary>
    private Dictionary<Id, List<Vehicle>> SeedFleets(List<Dealer> dealers, DateTimeOffset now)
    {
        var fleets = new Dictionary<Id, List<Vehicle>>();
        var plate = 30000;

        foreach (var dealer in dealers)
        {
            // Approval is the gate, not the ability to trade: a suspended dealer keeps the fleet it
            // built while it was in good standing.
            if (dealer.VerificationStatus != DealerVerificationStatus.Approved)
                continue;

            var fleet = new List<Vehicle>();
            var size = _random.Next(4, 13);

            for (var index = 0; index < size; index++)
            {
                var car = CarCatalogue[_random.Next(CarCatalogue.Length)];
                var year = now.Year - _random.Next(0, 6);
                var listedAt = now.AddDays(-_random.Next(20, 400));

                var details = VehicleDetails.Create(
                    car.Make,
                    car.Model,
                    year,
                    car.Seats,
                    _random.Next(100) < 80 ? TransmissionType.Automatic : TransmissionType.Manual,
                    FuelType.Petrol,
                    now.Year,
                    CarColours[_random.Next(CarColours.Length)],
                    $"{car.Make} {car.Model} {year}, maintained in-house and serviced between rentals.");
                if (details.IsFailure)
                    continue;

                var rate = Money.Jod(car.Low + (_random.Next(0, (int)(car.High - car.Low) + 1)));
                var mileage = _random.Next(100) < 30
                    ? MileagePolicy.Unlimited()
                    : MileagePolicy.Limited(200 + (_random.Next(0, 4) * 50), Money.Jod(0.15m)).Value;

                var vehicle = Vehicle.Add(
                    dealer.Id,
                    SeedCarTypeId,
                    details.Value,
                    PlateNumber.Create((plate++).ToString(System.Globalization.CultureInfo.InvariantCulture)).Value,
                    rate,
                    Money.Jod(100m + (_random.Next(0, 5) * 25m)),
                    mileage,
                    FuelPolicy.FullToFull,
                    isDeliveryEligible: _random.Next(100) < 60,
                    listedAt);
                if (vehicle.IsFailure)
                    continue;

                // A photo, because Publish refuses a listing without one -- the same rule a dealer
                // meets in the console. SeedAsync writes a placeholder file at this key afterwards.
                vehicle.Value.AddImage($"vehicles/{vehicle.Value.Id.Value}/seed-cover.jpg", listedAt);

                // Publishing is judged as of the day the car was LISTED, and on that day an approved
                // dealer was trading -- suspension came afterwards. Passing today's CanTrade instead
                // would leave the suspended dealer's whole lot sitting in Draft, a history that never
                // happened, and would hide the state the fleet screen exists to explain: a car that
                // is Active and still not reaching customers because the dealership cannot trade.
                ApplyFleetStatus(vehicle.Value, listedAt);
                fleet.Add(vehicle.Value);
            }

            fleets[dealer.Id] = fleet;
        }

        return fleets;
    }

    /// <summary>
    /// A fresh MileagePolicy with the same terms as the car's.
    ///
    /// The booking freezes the policy it was made under, and a frozen snapshot must be its own object:
    /// sharing the vehicle's instance would put one value object under two aggregates, which is the
    /// mistake this file has now made twice.
    /// </summary>
    private static MileagePolicy CopyOf(MileagePolicy policy) =>
        policy.IsUnlimited
            ? MileagePolicy.Unlimited()
            : MileagePolicy.Limited(
                policy.DailyLimitKm!.Value,
                Money.Create(policy.ExcessFeePerKm!.Amount, policy.ExcessFeePerKm.CurrencyCode)).Value;

    /// <summary>Puts a seeded car into a lifelike state, always through the domain's own methods.</summary>
    private void ApplyFleetStatus(Vehicle vehicle, DateTimeOffset listedAt)
    {
        var roll = _random.Next(100);

        // Left as a draft: started and never finished. Nothing else to do.
        if (roll < 8)
            return;

        vehicle.Publish(dealerCanTrade: true, listedAt.AddHours(2));

        if (roll >= 88 && roll < 95)
            vehicle.Hide(listedAt.AddDays(_random.Next(1, 30)));
        else if (roll >= 95)
            vehicle.SendToMaintenance(listedAt.AddDays(_random.Next(1, 30)));
    }

    private List<Booking> SeedBookings(
        List<Dealer> tradingDealers,
        Dictionary<Id, List<Vehicle>> fleets,
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
                var booking = CreateBooking(tradingDealers, fleets, customers, BuildTerms(rules), rules, createdAt);
                if (booking is not null)
                    bookings.Add(booking);
            }
        }

        return bookings;
    }

    private Booking? CreateBooking(
        List<Dealer> tradingDealers,
        Dictionary<Id, List<Vehicle>> fleets,
        List<User> customers,
        BookingTerms terms,
        BusinessRules rules,
        DateTimeOffset createdAt)
    {
        var dealer = tradingDealers[_random.Next(tradingDealers.Count)];
        var customer = customers[_random.Next(customers.Count)];

        // Customers book cars that are actually listed, so a booking's vehicle id resolves to a real
        // vehicle belonging to that same dealer. Drafts are excluded because a customer could never
        // have seen one.
        if (!fleets.TryGetValue(dealer.Id, out var fleet))
            return null;
        var bookable = fleet.Where(car => car.Status != VehicleStatus.Draft).ToList();
        if (bookable.Count == 0)
            return null;
        var vehicle = bookable[_random.Next(bookable.Count)];

        var days = _random.Next(2, 9);
        // The period must start after the booking was made; a couple of days of lead time is typical.
        var start = createdAt.AddDays(_random.Next(1, 4));
        var period = DateRange.Create(start, start.AddDays(days));
        if (period.IsFailure)
            return null;

        // The price is the CAR's price, not an invented one, and delivery is only offered on a car the
        // dealer marked eligible for it. That is what makes a seeded booking check out against the
        // vehicle it names when an admin opens the two side by side.
        var delivery = vehicle.IsDeliveryEligible && _random.Next(100) < 35;
        var pricing = BookingPricing.Calculate(
            Money.Create(vehicle.DailyRate.Amount, vehicle.DailyRate.CurrencyCode),
            period.Value.WholeDays,
            delivery ? Money.Jod(rules.DeliveryFee.Amount) : Money.Jod(0m),
            Percentage.FromValidated(rules.DepositPercent),
            Money.Create(vehicle.SecurityDeposit.Amount, vehicle.SecurityDeposit.CurrencyCode),
            CopyOf(vehicle.Mileage),
            vehicle.FuelPolicy);
        if (pricing.IsFailure)
            return null;

        var booking = Booking.Create(
            customer.Id,
            dealer.Id,
            vehicle.Id,
            period.Value,
            delivery ? PickupMethod.Delivery : PickupMethod.SelfPickup,
            // A COPY of the dealer's coordinates. Handing over dealer.Location itself put one GeoPoint
            // instance under two aggregates, which EF reported on every seed as the same entity being
            // tracked as two different types.
            delivery ? GeoPoint.Create(dealer.Location.Latitude, dealer.Location.Longitude).Value : null,
            pricing.Value,
            terms,
            PaymentOption.DepositOnly,
            createdAt);

        return booking.IsFailure ? null : Advance(booking.Value, dealer.OwnerUserId, rules, createdAt);
    }

    /// <summary>
    /// Walks a booking forward through its real lifecycle so the status mix looks like a live
    /// platform. Every transition is the domain method, at a plausible instant, which means the seeded
    /// data satisfies the same invariants a real booking would.
    /// </summary>
    private Booking Advance(Booking booking, Id dealerActorId, BusinessRules rules, DateTimeOffset createdAt)
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
            booking.Reject(dealerActorId, "The vehicle is already committed for those dates.", createdAt.AddHours(3));
            return booking;
        }

        var approvedAt = createdAt.AddHours(2);
        booking.Approve(dealerActorId, approvedAt);

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

        booking.RecordPickup(BookingParty.Dealer, dealerActorId, pickupDue.AddMinutes(20), odometerKm: 42_000, fuelLevel: 1m);

        var returnDue = booking.Period.End;
        if (returnDue > now)
            return booking; // Out on hire right now.

        booking.RecordReturn(BookingParty.Dealer, dealerActorId, returnDue.AddMinutes(15), odometerKm: 42_650, fuelLevel: 1m);

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
