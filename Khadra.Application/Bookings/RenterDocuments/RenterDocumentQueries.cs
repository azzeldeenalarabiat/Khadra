using CSharpFunctionalExtensions;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

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
/// </remarks>
public sealed record ViewRenterDocumentsQuery(Id ActorUserId, Id BookingId)
    : IQuery<Result<RenterDocumentsDto, Error>>;

/// <summary>
/// The bytes of ONE of those documents, for the gallery to look at during the handover.
/// </summary>
/// <remarks>
/// <b>Not a signed link, deliberately.</b> The platform's <c>IDocumentLinkSigner</c> authorises once
/// and delivers later, which is right where the grant is stable for a session — an administrator
/// reviewing a dealership, a customer opening their own passport, the two parties to a dispute
/// ticket. A gallery's right to a renter's licence is not stable: it is <c>Booking.IsLive(now)</c>,
/// which ends the instant a decision window closes or the car comes back. A five-minute link is a
/// five-minute grant that outlives the predicate which issued it, and its token carries the storage
/// key — <c>customers/{customerUserId}/…</c> — into a gallery's browser, handing them an identifier
/// the platform is otherwise careful never to give them.
///
/// So this streams instead, and re-runs the FULL check on every request. Pre-launch items 14 and 63
/// record why; read them before making these two paths "consistent".
/// </remarks>
public sealed record OpenRenterDocumentQuery(Id ActorUserId, Id BookingId, Id DocumentId)
    : IQuery<Result<OpenedDocument, Error>>;

/// <summary>An open stream of a private file, and what it is. Never a key and never a name.</summary>
/// <remarks>
/// No file NAME on purpose. A stored key is a generated guid, so any name would be invented — and the
/// one obvious source, the customer's original upload, is a string they chose and is not a gallery's
/// to receive.
/// </remarks>
public sealed record OpenedDocument(Stream Content, string ContentType);

/// <summary>
/// Both halves of the gallery's view of a renter's paperwork, sharing one access rule.
/// </summary>
/// <remarks>
/// One class rather than two handlers because the rule is the whole feature, and a rule copied is a
/// rule that drifts — the way it would drift is the listing tightening while the bytes stayed open.
/// </remarks>
public sealed partial class RenterDocumentHandlers(
    IBookingRepository bookings,
    IUserRepository users,
    IDocumentStorage storage,
    DealerMembershipResolver membership,
    IClock clock,
    ILogger<RenterDocumentHandlers> logger) :
    IRequestHandler<ViewRenterDocumentsQuery, Result<RenterDocumentsDto, Error>>,
    IRequestHandler<OpenRenterDocumentQuery, Result<OpenedDocument, Error>>
{
    public async Task<Result<RenterDocumentsDto, Error>> Handle(
        ViewRenterDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var renter = await ResolveAsync(request.ActorUserId, request.BookingId, cancellationToken);
        if (renter.IsFailure)
            return renter.Error;

        // A renter whose account has since gone is NOT "nothing on file". That reads as a customer
        // who never uploaded anything, and it would come back with an empty `missing` list -- a
        // combination no real customer can be in, and one a client could reasonably read as "the
        // paperwork is complete". A gallery about to hand a car to someone whose account has been
        // deleted is being told the opposite of the fact that matters, so this refuses instead.
        if (renter.Value is not { } customer)
            return IdentityErrors.UserNotFound;

        return new RenterDocumentsDto(
            [.. customer.Documents.OrderBy(document => document.Type.Id).Select(RenterDocumentDto.From)],
            customer.HasCompleteRenterDocuments,
            // The aggregate's own answer, so this list and the customer's own checklist cannot
            // disagree about what is outstanding.
            [.. customer.MissingRenterDocumentTypes().Select(type => type.Name)]);
    }

    public async Task<Result<OpenedDocument, Error>> Handle(
        OpenRenterDocumentQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var renter = await ResolveAsync(request.ActorUserId, request.BookingId, cancellationToken);
        if (renter.IsFailure)
            return renter.Error;

        // A document id belonging to somebody else is reported as NOT FOUND, exactly as it is on the
        // customer's own route. A 403 would confirm that the id names a real file, which is the one
        // thing a caller holding a guessed id must not learn. A vanished renter lands here too, and
        // gives the same answer for the same reason.
        var document = renter.Value?.FindDocument(request.DocumentId);
        if (document is null)
            return IdentityErrors.DocumentNotFound;

        var content = await storage.OpenAsync(document.StorageKey, cancellationToken);
        // The row survived and the blob did not. Not an authorization answer, and not a 500 either:
        // the caller was entitled to it, and there is nothing to send.
        if (content is null)
            return IdentityErrors.DocumentNotFound;

        // Spec 7 disclosure: a gallery has just opened a named private individual's identity paper.
        // Nothing else records it -- IAuditTrail is the ADMIN's trail and commits inside a writing
        // handler's transaction, which a read has none of -- so this log line is currently the only
        // account of who looked at what. Pre-launch item 86 asks the owner whether that needs to be
        // durable.
        LogDisclosed(
            logger,
            request.ActorUserId.Value,
            request.BookingId.Value,
            request.DocumentId.Value,
            document.Type.Name);

        return new OpenedDocument(content, DocumentContentTypes.ForStorageKey(document.StorageKey));
    }

    /// <summary>
    /// The renter of a booking this caller is entitled to see, or the reason they are not.
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
    private async Task<Result<User?, Error>> ResolveAsync(
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
        return await users.GetByIdAsync(booking.CustomerId, cancellationToken);
    }

    [LoggerMessage(
        4200,
        LogLevel.Information,
        "Renter document disclosed to dealer staff {ActorUserId} on booking {BookingId}: document {DocumentId} ({DocumentType})")]
    private static partial void LogDisclosed(
        ILogger logger,
        Guid actorUserId,
        Guid bookingId,
        Guid documentId,
        string documentType);
}
