using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Common;

namespace Khadra.Application.Shortlist.ReadModels;

/// <summary>
/// One saved car: either a car the customer can still see, or a marker that it is gone.
/// </summary>
/// <remarks>
/// <para>
/// A saved car can legitimately become invisible — hidden, in maintenance, its gallery suspended,
/// soft-deleted — and the public catalogue answers the same "no such car" to all of them, on purpose,
/// so nobody can walk unpublished inventory by trying ids. That leaves a saved entry with nothing to
/// render.
/// </para>
/// <para>
/// <b>The marker names nothing.</b> No make, no model, no reason. Naming it would need either a
/// snapshot taken at save time — which goes stale the first time a gallery corrects a listing, so the
/// screen would show last month's car — or a read that bypasses the soft-delete filter and the
/// visibility predicate, which is the enumeration oracle the catalogue is shaped to prevent. "No
/// longer listed", with the date it was saved and a way to remove it, is the honest rendering.
/// </para>
/// <para>
/// <b>And no availability.</b> A saved car has no dates attached, and
/// <c>CatalogueVehicle.IsAvailable</c> is null without a period for exactly that reason. The row
/// carries today's daily rate and says nothing about whether it is free.
/// </para>
/// </remarks>
/// <param name="Listing">The live listing, or null when the car is no longer one this customer sees.</param>
public sealed record SavedVehicle(Guid VehicleId, DateTimeOffset SavedAt, CatalogueListing? Listing)
{
    public bool IsStillListed => Listing is not null;
}

public interface IShortlistReader
{
    /// <summary>
    /// The customer's saved cars, newest save first, each live or marked gone.
    /// </summary>
    /// <remarks>
    /// The listing half goes through the SAME predicate the catalogue search uses, so a car visible
    /// in search is visible here and nowhere else. A second predicate written for this screen would
    /// drift, and the way it would drift is showing a suspended gallery's car to somebody who saved
    /// it before the suspension.
    /// </remarks>
    Task<IReadOnlyList<SavedVehicle>> ListAsync(
        Id customerId,
        CancellationToken cancellationToken = default);
}
