using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Common;

namespace Khadra.Application.Shortlist.ReadModels;

/// <summary>
/// Enough to name a saved car that is no longer one the customer may book.
/// </summary>
/// <remarks>
/// <para>
/// Four public fields and no fifth. No reason, no status, no image, no dealer id: a reason would
/// distinguish the cases the catalogue answers identically, an image URL is served from static
/// storage and would be a hidden car's photograph on the open internet, and an id would only invite
/// a tap-through to a page that 404s.
/// </para>
/// <para>
/// <b>Read LIVE, and unfiltered.</b> Not snapshotted at save time, which would show last month's
/// name the first time a gallery corrected a listing. And deliberately past the soft-delete filter:
/// with the filter respected, a deleted car would be the one de-listing reason this screen could
/// tell apart from the others, which is exactly the distinction the whole context refuses to draw.
/// </para>
/// </remarks>
public sealed record SavedVehicleIdentity(string Make, string Model, int Year, string GalleryName);

/// <summary>
/// One saved car: the car, and whether the customer may still book it.
/// </summary>
/// <remarks>
/// <para>
/// A saved car can legitimately stop being bookable — hidden, in maintenance, its gallery suspended,
/// soft-deleted — and the public catalogue answers the same "no such car" to all of them, on purpose,
/// so nobody can walk unpublished inventory by trying ids. The entry keeps its row through all of it:
/// <c>Maintenance → Hidden → Active</c> is an ordinary round trip for a gallery, and a list that
/// edited itself on the way through would lose a customer's choices without asking.
/// </para>
/// <para>
/// <b>It names the car and its gallery. It never names a reason.</b> Settled by the owner on
/// 2026-09-11: the row reads "Currently unavailable", with the car still recognisable, and offers no
/// way to start a booking. Naming the car is not the enumeration oracle this record once warned
/// about — <c>SaveVehicleCommand</c> refuses to save any id the public catalogue will not return, so
/// an entry can only be a car this customer was already shown. What would be an oracle is a reason,
/// and there is none here.
/// </para>
/// <para>
/// <b>And no availability.</b> A saved car has no dates attached, and
/// <c>CatalogueVehicle.IsAvailable</c> is null without a period for exactly that reason. A listed row
/// carries today's daily rate and says nothing about whether the car is free.
/// </para>
/// </remarks>
/// <param name="Identity">
/// The car and its gallery, for every entry whose rows still exist. Null only if a vehicle has been
/// removed from the database by hand, which nothing in this platform does — the screen then falls
/// back to naming nothing, because a placeholder title would be a figure the app invented.
/// </param>
/// <param name="Listing">The live listing, or null when the car is not one this customer may book.</param>
public sealed record SavedVehicle(
    Guid VehicleId,
    DateTimeOffset SavedAt,
    SavedVehicleIdentity? Identity,
    CatalogueListing? Listing)
{
    public bool IsStillListed => Listing is not null;
}

public interface IShortlistReader
{
    /// <summary>
    /// The customer's saved cars, newest save first, each one bookable or not.
    /// </summary>
    /// <remarks>
    /// The listing half goes through the SAME predicate the catalogue search uses, so a car bookable
    /// in search is bookable here and nowhere else. A second predicate written for this screen would
    /// drift, and the way it would drift is offering a suspended gallery's car to somebody who saved
    /// it before the suspension.
    ///
    /// The naming half has no predicate at all. Its only filter is "ids on this customer's own
    /// list", which is the shortlist's, so there is no second visibility rule here to drift from the
    /// first.
    /// </remarks>
    Task<IReadOnlyList<SavedVehicle>> ListAsync(
        Id customerId,
        CancellationToken cancellationToken = default);
}
