using Khadra.Application.Fleet.ReadModels;
using Khadra.Application.Shortlist.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Shortlist;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>
/// A customer's saved cars, each one live or marked gone.
/// </summary>
/// <remarks>
/// <para>
/// Two reads, never one per entry. The entries come from this context; the listings come from the
/// CATALOGUE's own by-ids read, which applies the same visibility predicate the search does. Looking
/// each car up through the public detail endpoint instead would be N requests and would show the
/// holes as errors.
/// </para>
/// <para>
/// The join is a LEFT one in effect: an id the catalogue did not return keeps its row, with a null
/// listing and the date it was saved. That is the whole reason this reader exists rather than the
/// screen calling the catalogue itself — the ABSENCE has to survive into the result, because it is
/// what the customer needs told.
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

        var listings = await catalogue.ListByIdsAsync(
            [.. entries.Select(entry => entry.VehicleId)],
            cancellationToken);

        var byId = listings.ToDictionary(listing => listing.VehicleId);

        return
        [
            .. entries.Select(entry => new SavedVehicle(
                entry.VehicleId.Value,
                entry.SavedAt,
                byId.GetValueOrDefault(entry.VehicleId.Value))),
        ];
    }
}
