using CSharpFunctionalExtensions;
using Khadra.Application.Auditing;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.Bookings.RenterDocuments;

/// <summary>
/// What the renter of one of this gallery's bookings has on file (spec 5.1, spec 7).
/// </summary>
/// <remarks>
/// <b>Keyed on the BOOKING, never on a customer or a document id alone, and that is the whole
/// authorization model rather than a convenience.</b> An endpoint taking a customer id would be a
/// document oracle over the entire customer base for anyone holding a dealer session, and no check
/// inside it could undo that — the check would have to be "do I have a live booking with this
/// person", which is exactly the booking the caller could have named instead. Naming the booking
/// makes the relationship the KEY, so there is nothing to enumerate: the customer is read off the
/// booking, and a document id that does not belong to that customer is simply not found.
///
/// Same argument, and the same shape, as <c>GetCustomerReputationQuery</c>.
///
/// Deliberately NOT audited. Unlike the two below, this fires on every booking-detail open without
/// anybody pressing anything (the console holds it in an <c>httpResource</c> keyed on the booking
/// being viewed), so logging it would fill the disclosure log with "views" the gallery never chose,
/// multiplied by every navigation. It describes what exists; it reveals nothing. Item 86 records
/// that limit rather than overclaiming "every access is logged".
/// </remarks>
public sealed record ViewRenterDocumentsQuery(Id ActorUserId, Id BookingId)
    : IQuery<Result<RenterDocumentsDto, Error>>;

/// <summary>
/// The bytes of ONE of those documents, for the gallery to look at during the handover.
/// </summary>
/// <remarks>
/// <para>
/// <b>A command, not a query, because it writes.</b> Releasing a private document is a disclosure,
/// and item 86 requires a durable record of it, so this stages a <c>Viewed</c> entry and commits it
/// BEFORE the bytes are streamed. Named honestly so nobody "fixes" the <c>SaveChangesAsync</c> out
/// of something called a query.
/// </para>
/// <para>
/// <b>Not a signed link, deliberately.</b> The platform's <c>IDocumentLinkSigner</c> authorises once
/// and delivers later, which is right where the grant is stable for a session — an administrator
/// reviewing a dealership, a customer opening their own passport, the two parties to a dispute
/// ticket. A gallery's right to a renter's licence is not stable: it is <c>Booking.IsLive(now)</c>,
/// which ends the instant a decision window closes or the car comes back. A five-minute link is a
/// five-minute grant that outlives the predicate which issued it, and its token carries the storage
/// key — <c>customers/{customerUserId}/…</c> — into a gallery's browser, handing them an identifier
/// the platform is otherwise careful never to give them.
/// </para>
/// <para>
/// So this streams instead, and re-runs the FULL check on every request. Pre-launch items 14 and 63
/// record why; read them before making these two paths "consistent".
/// </para>
/// </remarks>
public sealed record OpenRenterDocumentCommand(Id ActorUserId, Id BookingId, Id DocumentId)
    : ICommand<Result<OpenedDocument, Error>>;

/// <summary>
/// The dealership records that it CHECKED one of the renter's documents (spec 5.1).
/// </summary>
/// <remarks>
/// <para>
/// Nothing about this says the document is genuine. It says a named member of this gallery's staff
/// looked at the file the renter uploaded, at a moment the platform recorded. Khadra verifies
/// nothing — <c>CustomerDocument.MarkVerified</c> stays unreachable, and item 27 is where whether
/// anyone ever will gets decided.
/// </para>
/// <para>
/// The reviewer and the timestamp are NOT inputs. They come from the validated token and the server
/// clock, so there is nothing on the wire to forge: the request names a booking and a document, and
/// every other fact is established server-side.
/// </para>
/// </remarks>
public sealed record RecordRenterDocumentReviewCommand(Id ActorUserId, Id BookingId, Id DocumentId)
    : ICommand<Result<RenterDocumentDto, Error>>;

/// <summary>An open stream of a private file, and what it is. Never a key and never a name.</summary>
/// <remarks>
/// No file NAME on purpose. A stored key is a generated guid, so any name would be invented — and the
/// one obvious source, the customer's original upload, is a string they chose and is not a gallery's
/// to receive.
/// </remarks>
public sealed record OpenedDocument(Stream Content, string ContentType);

/// <summary>
/// Everything the gallery may do with a renter's paperwork, sharing one access rule.
/// </summary>
/// <remarks>
/// One class rather than three handlers because the rule is the whole feature, and a rule copied is a
/// rule that drifts — the way it would drift is the listing tightening while the bytes stayed open,
/// or the review outliving the view.
/// </remarks>
public sealed partial class RenterDocumentHandlers(
    IBookingRepository bookings,
    IUserRepository users,
    IDocumentStorage storage,
    DealerMembershipResolver membership,
    DocumentAccessRecorder accessLog,
    ICurrentActor actor,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<ViewRenterDocumentsQuery, Result<RenterDocumentsDto, Error>>,
    IRequestHandler<OpenRenterDocumentCommand, Result<OpenedDocument, Error>>,
    IRequestHandler<RecordRenterDocumentReviewCommand, Result<RenterDocumentDto, Error>>
{
    public async Task<Result<RenterDocumentsDto, Error>> Handle(
        ViewRenterDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await ResolveAsync(request.ActorUserId, request.BookingId, cancellationToken);
        if (access.IsFailure)
            return access.Error;

        var (booking, customer) = access.Value;

        // A renter whose account has since gone is NOT "nothing on file". That reads as a customer
        // who never uploaded anything, and it would come back with an empty `missing` list -- a
        // combination no real customer can be in, and one a client could reasonably read as "the
        // paperwork is complete". A gallery about to hand a car to a deleted account is being told
        // the opposite of the fact that matters, so this refuses instead.
        if (customer is null)
            return IdentityErrors.UserNotFound;

        return new RenterDocumentsDto(
            [.. customer.Documents
                .OrderBy(document => document.Type.Id)
                .Select(document => RenterDocumentDto.From(document, ReviewOf(booking, document)))],
            customer.HasCompleteRenterDocuments,
            // The aggregate's own answer, so this list and the customer's own checklist cannot
            // disagree about what is outstanding.
            [.. customer.MissingRenterDocumentTypes().Select(type => type.Name)]);
    }

    public async Task<Result<OpenedDocument, Error>> Handle(
        OpenRenterDocumentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await ResolveAsync(request.ActorUserId, request.BookingId, cancellationToken);
        if (access.IsFailure)
            return access.Error;

        var (booking, customer) = access.Value;

        // A document id belonging to somebody else is reported as NOT FOUND, exactly as it is on the
        // customer's own route. A 403 would confirm that the id names a real file, which is the one
        // thing a caller holding a guessed id must not learn. A vanished renter lands here too, and
        // gives the same answer for the same reason.
        var document = customer?.FindDocument(request.DocumentId);
        if (document is null)
            return IdentityErrors.DocumentNotFound;

        var content = await storage.OpenAsync(document.StorageKey, cancellationToken);
        // The row survived and the blob did not. Not an authorization answer, and not a 500 either:
        // the caller was entitled to it, there is nothing to send -- and nothing was disclosed, so
        // nothing is recorded.
        if (content is null)
            return IdentityErrors.DocumentNotFound;

        // Item 86: the disclosure is recorded BEFORE the bytes go out, and if it cannot be recorded
        // the bytes do not go out. That is the strict reading, and it costs almost nothing here: this
        // INSERT goes to the same database on the same connection as the membership, booking and user
        // reads that just authorised the request, so the window in which it can fail alone is a
        // connection dropped between two statements. The alternative -- serve anyway -- makes "the
        // log shows nobody looked" stop meaning anything during exactly the incidents somebody would
        // later be investigating.
        try
        {
            accessLog.Record(
                DocumentAccessAction.Viewed,
                booking.DealerId,
                booking.Id,
                booking.CustomerId,
                document.Id,
                document.Type,
                document.UploadedAt);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Nothing has been sent yet, so the handle is ours to close. Letting it escape here would
            // leak a storage stream on every failed write.
            await content.DisposeAsync();
            throw;
        }

        return new OpenedDocument(content, DocumentContentTypes.ForStorageKey(document.StorageKey));
    }

    public async Task<Result<RenterDocumentDto, Error>> Handle(
        RecordRenterDocumentReviewCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await ResolveAsync(request.ActorUserId, request.BookingId, cancellationToken);
        if (access.IsFailure)
            return access.Error;

        var (booking, customer) = access.Value;

        var document = customer?.FindDocument(request.DocumentId);
        if (document is null)
            return IdentityErrors.DocumentNotFound;

        // A repeat -- a double click, or a colleague pressing it after somebody already did -- answers
        // with the record that is already there rather than a conflict. The same reasoning as
        // CancelMyBookingCommand: a phone that lost the first response would otherwise be told its
        // click failed when it succeeded. Nothing is written, so nothing is logged: no new fact, no
        // new disclosure, and the FIRST look keeps its timestamp, which is the whole value of it.
        var existing = booking.FindRenterDocumentReview(document.Id, document.UploadedAt);
        if (existing is not null)
            return RenterDocumentDto.From(document, existing);

        var recorded = booking.RecordRenterDocumentReview(
            document.Id,
            document.Type,
            document.UploadedAt,
            request.ActorUserId,
            // From the validated token, never the request body. There is no field on the wire that
            // could name a different reviewer.
            actor.Name ?? "Unknown",
            clock.UtcNow);
        if (recorded.IsFailure)
            return recorded.Error;

        // Staged before the one save, so the review and its record land in the same transaction --
        // the rule CLAUDE.md states for the audit trail, applied unchanged. A behaviour running after
        // the handler would need a second save, which is exactly the "action without a record" the
        // rule exists to prevent.
        accessLog.Record(
            DocumentAccessAction.Reviewed,
            booking.DealerId,
            booking.Id,
            booking.CustomerId,
            document.Id,
            document.Type,
            document.UploadedAt);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RenterDocumentDto.From(document, recorded.Value);
    }

    /// <summary>
    /// This dealership's review of THIS upload of a document, or null.
    /// </summary>
    /// <remarks>
    /// The server answers it, not the screen. A console comparing a review date with an upload date
    /// would be re-deriving a rule that lives in the aggregate, and the two would drift in the
    /// direction that matters: a replaced photograph still showing as reviewed.
    /// </remarks>
    private static RenterDocumentReview? ReviewOf(Booking booking, CustomerDocument document) =>
        booking.FindRenterDocumentReview(document.Id, document.UploadedAt);

    /// <summary>
    /// The booking and the renter this caller is entitled to, or the reason they are not.
    /// </summary>
    /// <remarks>
    /// Three gates, in this order, and each one answers a different question.
    ///
    /// <b>1. Membership.</b> <c>DealerMembershipResolver</c>, so a DEACTIVATED employee is not staff:
    /// spec 4.2 ends their access immediately, and the raw repository lookup they would otherwise hit
    /// matches inactive rows on purpose.
    ///
    /// <b>2. The booking is this dealership's</b> — and <c>not_found</c> if it is not, never 403,
    /// exactly as everywhere else a booking is addressed by id. A 403 would confirm the id is real,
    /// which is how one gallery learns another's booking ids exist.
    ///
    /// <b>3. The booking is LIVE.</b> <c>Booking.IsLive</c> is the same predicate the reputation
    /// endpoint grants on and the same one availability asks in SQL: Requested before its decision
    /// deadline, Approved before its payment deadline, Confirmed, PickedUp. That is precisely the
    /// span in which this gallery is deciding about, waiting on, handing a car to, or holding a car
    /// with this person. Returned is settlement, not custody, and — with no settlement job yet
    /// (item 4) — a booking can sit in it indefinitely, so "through Returned" would mean "for ever".
    /// Reviewing is gated on exactly the same predicate, not a narrower one: at Requested the gallery
    /// is looking at the licence precisely in order to DECIDE, and refusing the record at the moment
    /// the look actually happens would make the record lie about when it happened.
    ///
    /// <b>What is deliberately NOT a gate: whether the dealership may still trade.</b>
    /// <c>DealerMembership.CanActOnBookings</c> guards approve and reject, and the reputation
    /// endpoint borrows it. Borrowing it here would be a real hole: a suspended gallery may still
    /// record a pickup (<c>LoadForHandoverAsync</c>, and <c>POST /bookings/{id}/pickup</c> carries
    /// only <c>DealerStaff</c>), so gating the licence on trading would leave them handing a car to a
    /// stranger while the platform refused to show them who the stranger is. That is the exact
    /// failure item 63 exists to close. <c>CustomerDocument</c>'s own rule says "a dealer holding an
    /// active booking request" and says nothing about trading.
    /// </remarks>
    private async Task<Result<(Booking Booking, User? Customer), Error>> ResolveAsync(
        Id actorUserId,
        Id bookingId,
        CancellationToken cancellationToken)
    {
        var member = await membership.ResolveAsync(actorUserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;

        var booking = await bookings.GetByIdAsync(bookingId, cancellationToken);
        if (booking is null || booking.DealerId != member.Value.Dealer.Id)
            return BookingErrors.NotFound;

        if (!booking.IsLive(clock.UtcNow))
            return BookingErrors.RenterDocumentsNotAvailable;

        // The customer is read off the BOOKING. Nothing the caller sent names them, so there is no id
        // to substitute and no relationship to forge. Null -- a soft-deleted account -- is handled by
        // each caller rather than here, because "nothing on file" and "no such document" are the two
        // right answers and they are not the same answer.
        var customer = await users.GetByIdAsync(booking.CustomerId, cancellationToken);
        return (booking, customer);
    }
}
