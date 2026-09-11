using Khadra.Domain.Common;

namespace Khadra.Domain.Shortlist;

// Stable machine codes for every expected Shortlist failure. The API maps `Kind` to an HTTP status;
// clients map `Code` to a localised message.
public static class ShortlistErrors
{
    /// <summary>
    /// The car is not one this caller may save.
    /// </summary>
    /// <remarks>
    /// Deliberately the SAME answer for a car that does not exist, one still in draft, a hidden one,
    /// one in maintenance, one whose gallery is suspended or was never approved, and one that was
    /// soft-deleted — exactly the set <c>ICatalogueReader.GetAsync</c> already answers null to.
    ///
    /// The reason is that saving is the cheapest enumeration oracle on this platform. "Saved" on an
    /// id that answers 404 from the public catalogue would tell anybody with an account which ids
    /// exist, and a competitor could walk another gallery's unpublished inventory a request at a
    /// time. The visibility check goes through the catalogue's own predicate rather than a second one
    /// written here, because two predicates drift and only one of them is the one the customer can
    /// see.
    /// </remarks>
    public static readonly Error VehicleNotAvailable =
        Error.NotFound("shortlist.vehicle_not_found", "That car was not found.");

    /// <summary>The list is as long as the platform allows.</summary>
    /// <remarks>
    /// Carries the configured figure so a client can say WHY without hard-coding the platform's rule.
    /// </remarks>
    public static Error Full(int maximum) =>
        Error.Validation(
            "shortlist.full",
            $"A shortlist can hold at most {maximum} cars. Remove one before saving another.");
}
