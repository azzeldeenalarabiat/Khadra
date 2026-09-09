using Khadra.Domain.Common;

namespace Khadra.Application.Reviews.ReadModels;

/// <summary>
/// What one gallery may know about a customer while deciding whether to hand them a car.
/// </summary>
/// <remarks>
/// <para>
/// Spec 5.6 makes reviews mutual and says the dealer's direction informs approve/reject decisions. It
/// does not say what a gallery gets to READ, and the answer here is deliberately narrow: this is
/// unverified information about a named private individual, circulating between competing businesses,
/// which the individual cannot see. Every field below has to earn its place by changing a booking
/// decision.
/// </para>
/// <para>
/// <b>What is deliberately absent, and why.</b> No contact details, no date of birth or age (the
/// platform already enforces the minimum at registration, so a gallery learns nothing by seeing it),
/// no nationality flag, no document state, no per-review row, no reviewer identity, no gallery
/// identity, and NO TIMESTAMPS on individual ratings. That last one is not fussiness: a rating dated
/// last Tuesday tells this gallery when the customer rented from somebody else, which is
/// cross-gallery surveillance dressed as reputation. Aggregates only, and there is no list endpoint
/// for a dealer to page through — not now and not later.
/// </para>
/// <para>
/// <b>There is no free text anywhere in the dealer direction</b>, and none is stored. The platform
/// already made this call twice: rejection and cancellation reasons were free text, produced "asdf",
/// and were replaced by closed codes nobody has to read to count. Unverifiable prose about a named
/// person, written by a competitor, moderated by nobody at the moment it is written, would be worse
/// than either.
/// </para>
/// <para>
/// <b>Counts come from what the PLATFORM adjudicated, not from what a gallery asserted.</b> A no-show
/// or a late cancellation is a fact the aggregate recorded against its own frozen terms, with an
/// attribution it was careful about — a DELIVERY no-show is `Unattributed`, because the gallery was
/// the party who had to travel, and `ReportDealerNonDelivery` cancels with `CancelledBy = Customer`
/// while attributing the penalty to the DEALER. Counting by status would blame customers the domain
/// explicitly refused to blame, so every count here reads
/// <c>Penalty.AttributedTo == Customer</c> instead.
/// </para>
/// </remarks>
/// <param name="DealerRating">
/// The average and count of REVEALED, unhidden dealer ratings. Null average when nobody has rated
/// them: zero is a real score on a one-to-five scale and would read as the worst possible.
/// </param>
/// <param name="CompletedRentals">Rentals finished across the whole platform.</param>
/// <param name="CompletedRentalsWithThisDealer">
/// How many of those were with the gallery asking. A returning customer is the single most useful
/// fact on this panel, and it is the one piece of history a gallery already knows anyway.
/// </param>
/// <param name="NoShows">Rentals the platform recorded as a no-show ATTRIBUTED TO THE CUSTOMER.</param>
/// <param name="LateCancellations">
/// Cancellations that cost the customer something — a penalty was assessed against them. A free
/// cancellation inside the window is not a mark against anybody and is not counted.
/// </param>
/// <param name="DisputesResolvedAgainstCustomer">
/// Tickets an administrator resolved without returning the whole deposit. Rare, and the only figure
/// here that reflects a human decision rather than a clock.
/// </param>
/// <param name="CustomerSince">
/// When the account was created. A three-year-old account with no history reads very differently from
/// one made this morning, and it is the cheapest signal against a throwaway account.
/// </param>
public sealed record CustomerReputation(
    RatingSummary DealerRating,
    int CompletedRentals,
    int CompletedRentalsWithThisDealer,
    int NoShows,
    int LateCancellations,
    int DisputesResolvedAgainstCustomer,
    DateTimeOffset CustomerSince)
{
    /// <summary>
    /// Whether the platform has anything to say about this customer at all.
    /// </summary>
    /// <remarks>
    /// So a screen can say "no history on the platform yet" rather than a row of zeros, which reads
    /// like a bad record instead of an absent one.
    /// </remarks>
    public bool HasHistory =>
        DealerRating.Count > 0 ||
        CompletedRentals > 0 ||
        NoShows > 0 ||
        LateCancellations > 0 ||
        DisputesResolvedAgainstCustomer > 0;
}

public interface ICustomerReputationReader
{
    /// <param name="viewingDealerId">
    /// Whose "with this dealer" count to compute. Taken from the resolved membership of the caller,
    /// never from the request.
    /// </param>
    /// <param name="now">
    /// The instant visibility is judged at. A parameter rather than a clock the reader holds, so the
    /// blind window is decided by the same clock the rest of the request used.
    /// </param>
    Task<CustomerReputation> GetAsync(
        Id customerId,
        Id viewingDealerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
