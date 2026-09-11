using Khadra.Application.Fleet.ReadModels;
using Khadra.Application.Shortlist.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Shortlist;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>
/// A customer's saved cars, each one bookable or not, and every one of them named.
/// </summary>
/// <remarks>
/// <para>
/// Three reads, never one per entry. The entries come from this context. The listings come from the
/// CATALOGUE's own by-ids read, which applies the same visibility predicate the search does. And the
/// names come from a third read that applies no predicate at all.
/// </para>
/// <para>
/// The listing join is a LEFT one in effect: an id the catalogue did not return keeps its row with a
/// null listing. That is the whole reason this reader exists rather than the screen calling the
/// catalogue itself — the ABSENCE has to survive into the result, because it is what the customer
/// needs told.
/// </para>
/// </remarks>
internal sealed class ShortlistReader(KhadraDbContext context, ICatalogueReader catalogue)
    : IShortlistReader
{
    public async Task<IReadOnlyList<SavedVehicle>> ListAsync(
        Id customerId,
        CancellationToken cancellationToken = default)
    {
        // Straight at the child table through the owner column. Newest save first, with the id
        // breaking the tie: `SavedAt` is not unique — two taps in the same second are ordinary — and
        // a non-total order makes the list reshuffle between reads.
        var entries = await context.Set<ShortlistEntry>()
            .AsNoTracking()
            .Where(entry => entry.ShortlistId == customerId)
            .OrderByDescending(entry => entry.SavedAt)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new { entry.VehicleId, entry.SavedAt })
            .ToListAsync(cancellationToken);

        if (entries.Count == 0) return [];

        var ids = entries.ConvertAll(entry => entry.VehicleId);

        var listings = await catalogue.ListByIdsAsync(ids, cancellationToken);
        var byId = listings.ToDictionary(listing => listing.VehicleId);

        var identities = await NamesAsync(ids, cancellationToken);

        return
        [
            .. entries.Select(entry => new SavedVehicle(
                entry.VehicleId.Value,
                entry.SavedAt,
                identities.GetValueOrDefault(entry.VehicleId.Value),
                byId.GetValueOrDefault(entry.VehicleId.Value))),
        ];
    }

    /// <summary>
    /// What each of these cars is called, and whose it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>IgnoreQueryFilters, deliberately, with a reason that is about correctness rather than
    /// convenience.</b> This platform answers "no such car" identically for a hidden car, one in
    /// maintenance, a suspended gallery's and a soft-deleted one — that indistinguishability is what
    /// <c>SaveVehicleCommand</c>, <c>ShortlistErrors.VehicleNotAvailable</c> and the whole catalogue
    /// predicate exist to protect. If this read respected the soft-delete filter, a deleted car would
    /// come back unnamed while every other de-listed car came back named, and DELETION would become
    /// the one reason this screen could tell apart. Bypassing the filter is what keeps them alike.
    /// </para>
    /// <para>
    /// It is safe to run without a visibility predicate because its only filter is the customer's own
    /// shortlist, and an id cannot get onto a shortlist without the public catalogue having returned
    /// it: <c>SaveVehicleCommand</c> refuses anything <c>ICatalogueReader.GetAsync</c> answers null
    /// to. Every name this returns is a car this customer was already shown.
    /// </para>
    /// <para>
    /// All or nothing per car: a vehicle row whose gallery cannot be found yields no identity rather
    /// than a half-named one, because a card with a car and no gallery reads as a gallery this
    /// platform has lost.
    /// </para>
    /// </remarks>
    private async Task<Dictionary<Guid, SavedVehicleIdentity>> NamesAsync(
        IReadOnlyCollection<Id> vehicleIds,
        CancellationToken cancellationToken)
    {
        // Materialised first: EF translates Contains over a local List, not over an
        // IReadOnlyCollection it cannot recognise as a parameter.
        var wanted = vehicleIds.ToList();

        var named = await context.Vehicles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(vehicle => wanted.Contains(vehicle.Id))
            .Select(vehicle => new
            {
                Vehicle = vehicle.Id.Value,
                vehicle.Details.Make,
                vehicle.Details.Model,
                vehicle.Details.Year,
                // Unfiltered too — IgnoreQueryFilters is a query-level flag, so a soft-deleted
                // gallery's name comes back with its cars rather than leaving them half-named.
                GalleryName = context.Dealers
                    .Where(dealer => dealer.Id == vehicle.DealerId)
                    .Select(dealer => dealer.BusinessName.Value)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return named
            .Where(row => row.GalleryName is not null)
            .ToDictionary(
                row => row.Vehicle,
                row => new SavedVehicleIdentity(row.Make, row.Model, row.Year, row.GalleryName!));
    }
}
