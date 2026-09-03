using CSharpFunctionalExtensions;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;

namespace Khadra.Application.Bookings;

/// <summary>
/// Which side of a booking the signed-in person stands on, if any.
///
/// Four different handlers need this exact question answered -- reading a booking, opening a
/// dispute on it, adding a statement, withdrawing one -- and an authorisation rule copied into four
/// places is a rule that drifts. So it lives here once, and every caller gets the same answer.
///
/// The answer is a BookingParty, not a boolean: the ticket records WHO opened it, and a dealer
/// employee speaks for the dealer, not for themselves.
/// </summary>
public sealed class BookingPartyResolver(IDealerRepository dealers)
{
    /// <summary>
    /// Fails with booking.not_found for anyone who is not a party to the booking. Not 403: a 403 would
    /// confirm that the id exists, and a stranger probing ids should learn nothing either way.
    /// </summary>
    public async Task<Result<BookingParty, Error>> ResolveAsync(
        Booking booking,
        Id actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        if (booking.CustomerId == actorUserId)
            return BookingParty.Customer;

        var dealer = await dealers.GetByOwnerUserIdAsync(actorUserId, cancellationToken)
            ?? await dealers.GetByStaffUserIdAsync(actorUserId, cancellationToken);

        if (dealer is null || dealer.Id != booking.DealerId)
            return BookingErrors.NotFound;

        // The owner always speaks for the dealership. An employee does only while active: a
        // deactivated one keeps their login but not their standing (spec 4.2). Deliberately NOT
        // Dealer.CanActOnBookings, which also requires the dealer to be trading -- a suspended
        // dealer must still be able to see and dispute the bookings it already has.
        var speaksForDealer =
            dealer.OwnerUserId == actorUserId ||
            dealer.Employees.Any(employee => employee.UserId == actorUserId && employee.IsActive);

        return speaksForDealer ? BookingParty.Dealer : BookingErrors.NotFound;
    }
}
