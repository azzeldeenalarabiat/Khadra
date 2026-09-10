using Khadra.Application.Bookings.RenterDocuments;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Khadra.Tests.Application.Bookings;

/// <summary>
/// A gallery looking at the licence of the person they are about to hand a car to (spec 5.1).
/// </summary>
/// <remarks>
/// Nearly every test here is about somebody NOT being allowed to see something, and that is the
/// shape of the feature rather than an accident of what was easy to write. These are photographs of
/// a named private individual's passport and driving licence, and the access rule is the only thing
/// between them and any account that can reach a dealer session.
///
/// The rule, in one line: the booking is this dealership's, and it is LIVE. Not "this dealership may
/// trade" — see <see cref="A_suspended_dealership_can_still_see_the_licence_of_a_car_it_is_holding"/>,
/// which is the test that stops somebody tightening this to the approve gate and thereby making a
/// gallery hand over a car it is forbidden to check.
/// </remarks>
public sealed class RenterDocumentAccessTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private sealed class Context
    {
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public FakeDocumentStorage Storage { get; } = new();
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public TestClock Clock { get; } = new(Now);

        public Dealer Dealer { get; }
        public Id OwnerUserId { get; }
        public User Renter { get; }

        public Context(bool trading = true)
        {
            OwnerUserId = Id.New();
            Dealer = trading ? Build.ApprovedDealer(Now, OwnerUserId) : Suspended(OwnerUserId);
            Dealers.GetByOwnerUserIdAsync(OwnerUserId, Arg.Any<CancellationToken>()).Returns(Dealer);

            Renter = Build.Customer(Now);
            Users.GetByIdAsync(Renter.Id, Arg.Any<CancellationToken>()).Returns(Renter);
        }

        private static Dealer Suspended(Id ownerUserId)
        {
            var dealer = Build.ApprovedDealer(Now, ownerUserId);
            dealer.Suspend(Id.New(), "Under investigation.", Now);
            return dealer;
        }

        /// <summary>Hires an employee and makes the repository answer for them the way EF would.</summary>
        public Id HireEmployee(bool active = true)
        {
            var userId = Id.New();
            var employee = Dealer.HireEmployee(userId, canViewReports: false, Now).Value;
            if (!active)
                Dealer.DeactivateEmployee(employee.Id, Now);

            // The staff lookup matches an inactive row too, on purpose: the resolver is what decides
            // they have no standing, and these tests exist to prove it still does.
            Dealers.GetByStaffUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(Dealer);
            return userId;
        }

        public Booking Given(Booking booking)
        {
            Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
            return booking;
        }

        public RenterDocumentHandlers Handlers() => new(
            Bookings,
            Users,
            Storage,
            new DealerMembershipResolver(Dealers),
            Clock,
            NullLogger<RenterDocumentHandlers>.Instance);
    }

    private static Booking Confirmed(Context context) =>
        context.Given(Build.ConfirmedBooking(
            Now, customerId: context.Renter.Id, dealerId: context.Dealer.Id));

    private static Booking Requested(Context context) =>
        context.Given(Build.Booking(Now, customerId: context.Renter.Id, dealerId: context.Dealer.Id));

    // ── Who may look ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_owner_of_the_dealership_holding_the_car_sees_the_paperwork()
    {
        var context = new Context();
        var booking = Confirmed(context);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsSuccess);
        // Both sides of the licence and one identity document: what spec 5.1 asks a renter for.
        Assert.Equal(3, result.Value.Documents.Count);
        Assert.True(result.Value.IsComplete);
        Assert.Empty(result.Value.Missing);
    }

    [Fact]
    public async Task An_active_employee_sees_it_too_because_they_are_the_one_at_the_counter()
    {
        // Spec 4.2 gives an employee the booking desk, and POST /bookings/{id}/pickup asks only for
        // membership. An employee who can hand the car over but cannot check the licence would be
        // item 63 reopened one role down.
        var context = new Context();
        var employeeUserId = context.HireEmployee();
        var booking = Confirmed(context);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(employeeUserId, booking.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Documents.Count);
    }

    [Fact]
    public async Task A_deactivated_employee_has_no_standing_and_is_refused()
    {
        // Spec 4.2: their access ends immediately. The repository still answers for them -- their
        // past decisions name them -- so this is the resolver's job, not the lookup's.
        var context = new Context();
        var employeeUserId = context.HireEmployee(active: false);
        var booking = Confirmed(context);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(employeeUserId, booking.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(DealerErrors.NotRegistered.Code, result.Error.Code);
    }

    [Fact]
    public async Task A_different_dealership_is_told_the_booking_does_not_exist()
    {
        // The headline rule: no dealer reaches another dealer's customer documents. NOT FOUND rather
        // than forbidden, so a gallery probing ids cannot tell a real booking from an invented one.
        var context = new Context();
        var strangerOwnerId = Id.New();
        var stranger = Build.ApprovedDealer(Now, strangerOwnerId, "Zarqa Auto Lease", "654321");
        context.Dealers.GetByOwnerUserIdAsync(strangerOwnerId, Arg.Any<CancellationToken>()).Returns(stranger);
        var booking = Confirmed(context);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(strangerOwnerId, booking.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.NotFound.Code, result.Error.Code);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
    }

    [Fact]
    public async Task A_different_dealership_cannot_reach_the_bytes_either()
    {
        // The same probe against the streaming route. The listing and the stream re-run the SAME
        // check; neither inherits a verdict from the other.
        var context = new Context();
        var strangerOwnerId = Id.New();
        var stranger = Build.ApprovedDealer(Now, strangerOwnerId, "Zarqa Auto Lease", "654321");
        context.Dealers.GetByOwnerUserIdAsync(strangerOwnerId, Arg.Any<CancellationToken>()).Returns(stranger);
        var booking = Confirmed(context);
        var licence = context.Renter.Documents.First(document => document.Type.IsLicence);

        var result = await context.Handlers().Handle(
            new OpenRenterDocumentQuery(strangerOwnerId, booking.Id, licence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Somebody_with_no_dealership_at_all_is_refused()
    {
        var context = new Context();
        var booking = Confirmed(context);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(Id.New(), booking.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(DealerErrors.NotRegistered.Code, result.Error.Code);
    }

    [Fact]
    public async Task A_suspended_dealership_can_still_see_the_licence_of_a_car_it_is_holding()
    {
        // THE test that pins the gate. A suspension stops NEW business; it does not stop a handover
        // on a booking already approved, and POST /bookings/{id}/pickup is deliberately not gated on
        // trading. Tightening this to DealerMembership.CanActOnBookings -- which the reputation
        // endpoint uses, and which is the obvious thing to copy -- would leave a suspended gallery
        // handing a car to a stranger while the platform refused to show them who the stranger is.
        var context = new Context(trading: false);
        var booking = Confirmed(context);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Documents.Count);
    }

    // ── For how long ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_deactivated_employee_cannot_reach_the_bytes_either()
    {
        // The stream re-runs the whole rule rather than trusting a listing that succeeded earlier,
        // and an employee let go between the two is exactly the case that distinguishes those.
        var context = new Context();
        var employeeUserId = context.HireEmployee(active: false);
        var booking = Confirmed(context);
        var licence = context.Renter.Documents.First(document => document.Type.IsLicence);

        var result = await context.Handlers().Handle(
            new OpenRenterDocumentQuery(employeeUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(DealerErrors.NotRegistered.Code, result.Error.Code);
    }

    [Fact]
    public async Task The_car_being_out_on_rental_still_grants_access()
    {
        // PickedUp is the handover state itself and the one this feature exists for. Every other
        // positive test uses Confirmed or Requested, so without this the state at the counter is the
        // one nothing covers.
        var context = new Context();
        var booking = Confirmed(context);
        booking.RecordPickup(BookingParty.Dealer, context.OwnerUserId, Now);
        context.Given(booking);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Documents.Count);
    }

    [Fact]
    public async Task An_approval_still_inside_its_payment_window_grants_access()
    {
        var context = new Context();
        var booking = context.Given(Build.ApprovedBooking(
            Now, customerId: context.Renter.Id, dealerId: context.Dealer.Id));

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_request_still_inside_its_answer_window_is_enough()
    {
        // CustomerDocument's own rule: "a dealer holding an active booking request from them". The
        // gallery is deciding, and the licence is part of what they are deciding on.
        var context = new Context();
        var booking = Requested(context);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Access_ends_when_the_answer_window_closes_even_though_the_row_still_says_Requested()
    {
        // The clock has already decided; only the status is behind. Access must end with the CLOCK,
        // not with whatever job eventually updates the row -- there is no such job for this today.
        var context = new Context();
        var booking = Requested(context);
        context.Clock.UtcNow = booking.DecisionDeadline.AddSeconds(1);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.RenterDocumentsNotAvailable.Code, result.Error.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
    }

    [Fact]
    public async Task An_approval_whose_payment_window_lapsed_no_longer_grants_access()
    {
        var context = new Context();
        var booking = context.Given(Build.ApprovedBooking(
            Now, customerId: context.Renter.Id, dealerId: context.Dealer.Id));
        context.Clock.UtcNow = booking.PaymentDeadline!.Value.AddSeconds(1);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.RenterDocumentsNotAvailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task Once_the_car_is_back_the_gallery_no_longer_sees_the_renters_papers()
    {
        // Returned is settlement, not custody. The gallery had the whole rental to look; a dispute
        // goes through the ticket and an administrator.
        var context = new Context();
        var booking = Confirmed(context);
        booking.RecordPickup(BookingParty.Dealer, context.OwnerUserId, Now);
        booking.RecordReturn(BookingParty.Dealer, context.OwnerUserId, Now.AddDays(1));
        context.Given(booking);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.RenterDocumentsNotAvailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task A_cancelled_booking_closes_the_window_immediately()
    {
        var context = new Context();
        var booking = Requested(context);
        booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now);
        context.Given(booking);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.RenterDocumentsNotAvailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task The_stream_is_refused_once_the_booking_stops_being_live()
    {
        // A handover screen holds a document id for minutes. The listing that produced it succeeded;
        // this must still refuse, because the check is re-run rather than inherited.
        var context = new Context();
        var booking = Requested(context);
        var licence = context.Renter.Documents.First(document => document.Type.IsLicence);
        context.Clock.UtcNow = booking.DecisionDeadline.AddSeconds(1);

        var result = await context.Handlers().Handle(
            new OpenRenterDocumentQuery(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.RenterDocumentsNotAvailable.Code, result.Error.Code);
    }

    // ── Which document ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_bytes_come_back_with_the_type_the_listing_promised()
    {
        var context = new Context();
        var booking = Confirmed(context);
        var licence = context.Renter.Documents.First(
            document => document.Type == CustomerDocumentType.DrivingLicenceFront);

        var result = await context.Handlers().Handle(
            new OpenRenterDocumentQuery(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("image/jpeg", result.Value.ContentType);
        Assert.NotNull(result.Value.Content);
    }

    [Fact]
    public async Task A_document_id_belonging_to_another_customer_is_not_found()
    {
        // Swapping the document id in the URL is the obvious attack, and it must not even confirm
        // that the id names a real file. NOT FOUND, never forbidden.
        var context = new Context();
        var booking = Confirmed(context);
        var somebodyElse = Build.Customer(Now, email: "omar@example.jo", phone: "0797654321");
        var theirLicence = somebodyElse.Documents.First(document => document.Type.IsLicence);

        var result = await context.Handlers().Handle(
            new OpenRenterDocumentQuery(context.OwnerUserId, booking.Id, theirLicence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.DocumentNotFound.Code, result.Error.Code);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
    }

    [Fact]
    public async Task An_invented_document_id_is_not_found()
    {
        var context = new Context();
        var booking = Confirmed(context);

        var result = await context.Handlers().Handle(
            new OpenRenterDocumentQuery(context.OwnerUserId, booking.Id, Id.New()), default);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.DocumentNotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task The_listing_and_the_stream_agree_about_what_a_document_is()
    {
        // Both sides derive the content type from the KEY, so a licence stored as a PDF cannot be
        // labelled as a photograph on the tile and then served as a PDF. The admin review screen made
        // exactly that mistake by guessing from the document's type.
        var context = new Context();
        var renter = Build.Customer(Now, hasLicence: false, hasIdentity: false);
        renter.AttachDocument(
            CustomerDocumentType.DrivingLicenceFront,
            "customers/01a08222-0228-7f9f-b051-fb28aca2ac4c/scan.pdf",
            "application/pdf",
            2048,
            Now);
        context.Users.GetByIdAsync(renter.Id, Arg.Any<CancellationToken>()).Returns(renter);
        var booking = context.Given(Build.ConfirmedBooking(
            Now, customerId: renter.Id, dealerId: context.Dealer.Id));
        var listing = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        var opened = await context.Handlers().Handle(
            new OpenRenterDocumentQuery(
                context.OwnerUserId, booking.Id, Id.From(listing.Value.Documents[0].DocumentId)),
            default);

        Assert.Equal("application/pdf", listing.Value.Documents[0].ContentType);
        Assert.Equal(listing.Value.Documents[0].ContentType, opened.Value.ContentType);
    }

    [Fact]
    public async Task A_renter_whose_account_has_gone_is_refused_rather_than_reported_as_empty()
    {
        // "Nothing on file" would be a lie with consequences: it is what a customer who never
        // uploaded anything looks like, and it would come back with an EMPTY missing list -- a
        // combination no real customer can be in, and one a client could read as "complete". A
        // gallery about to hand over a car to a deleted account must not be told the paperwork is
        // fine.
        var context = new Context();
        var booking = Confirmed(context);
        context.Users.GetByIdAsync(context.Renter.Id, Arg.Any<CancellationToken>()).Returns((User?)null);

        var listing = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);
        var opened = await context.Handlers().Handle(
            new OpenRenterDocumentQuery(context.OwnerUserId, booking.Id, Id.New()), default);

        Assert.True(listing.IsFailure);
        Assert.Equal(IdentityErrors.UserNotFound.Code, listing.Error.Code);
        // The stream still says "no such document", which is the answer that discloses least.
        Assert.True(opened.IsFailure);
        Assert.Equal(IdentityErrors.DocumentNotFound.Code, opened.Error.Code);
    }

    [Fact]
    public async Task A_row_whose_file_has_gone_is_not_found_rather_than_a_broken_platform()
    {
        var context = new Context();
        var booking = Confirmed(context);
        var licence = context.Renter.Documents.First(document => document.Type.IsLicence);
        var storage = Substitute.For<IDocumentStorage>();
        storage.OpenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var handlers = new RenterDocumentHandlers(
            context.Bookings,
            context.Users,
            storage,
            new DealerMembershipResolver(context.Dealers),
            context.Clock,
            NullLogger<RenterDocumentHandlers>.Instance);

        var result = await handlers.Handle(
            new OpenRenterDocumentQuery(context.OwnerUserId, booking.Id, licence.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.DocumentNotFound.Code, result.Error.Code);
    }

    // ── What the listing says ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_renter_who_has_uploaded_nothing_is_an_empty_list_not_an_error()
    {
        // The booking exists and the gallery may see it; "they have filed nothing" is a true answer
        // about it. A 404 would make the screen unable to tell that apart from a booking that is not
        // theirs, and it needs to, to decide what to say.
        var context = new Context();
        var bare = Build.Customer(Now, hasLicence: false, hasIdentity: false);
        context.Users.GetByIdAsync(bare.Id, Arg.Any<CancellationToken>()).Returns(bare);
        var booking = context.Given(Build.ConfirmedBooking(
            Now, customerId: bare.Id, dealerId: context.Dealer.Id));

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Documents);
        Assert.False(result.Value.IsComplete);
        Assert.Equal(
            ["DrivingLicenceFront", "DrivingLicenceBack", "NationalId"],
            result.Value.Missing);
    }

    [Fact]
    public async Task A_half_finished_renter_names_exactly_what_is_absent()
    {
        var context = new Context();
        var partial = Build.Customer(Now, hasIdentity: false);
        context.Users.GetByIdAsync(partial.Id, Arg.Any<CancellationToken>()).Returns(partial);
        var booking = context.Given(Build.ConfirmedBooking(
            Now, customerId: partial.Id, dealerId: context.Dealer.Id));

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Documents.Count);
        Assert.Equal(["NationalId"], result.Value.Missing);
    }

    [Fact]
    public async Task The_listing_carries_no_storage_key_and_no_url()
    {
        // Enforced by the type -- RenterDocumentDto has nowhere to put one -- and asserted anyway,
        // because the failure this guards against is somebody ADDING a field for convenience. Every
        // string on the shape is checked against the key the document actually has.
        var context = new Context();
        var booking = Confirmed(context);
        var keys = context.Renter.Documents.Select(document => document.StorageKey).ToList();

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsSuccess);
        var rendered = System.Text.Json.JsonSerializer.Serialize(result.Value);
        foreach (var key in keys)
            Assert.DoesNotContain(key, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("supabase", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_review_note_written_for_the_customer_is_not_shown_to_the_gallery()
    {
        // A rejection note is addressed to the customer -- "the photograph is unreadable" -- and is
        // none of a gallery's business. CustomerDocumentDto carries it; this shape must not.
        var context = new Context();
        var booking = Confirmed(context);

        var result = await context.Handlers().Handle(
            new ViewRenterDocumentsQuery(context.OwnerUserId, booking.Id), default);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(
            "reviewNote",
            System.Text.Json.JsonSerializer.Serialize(result.Value),
            StringComparison.OrdinalIgnoreCase);
    }
}
