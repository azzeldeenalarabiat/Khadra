using Khadra.Domain.Common;
using Khadra.Domain.Shortlist;
using Khadra.Domain.Shortlist.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

internal sealed class ShortlistRepository(KhadraDbContext context) : IShortlistRepository
{
    public Task<CustomerShortlist?> GetAsync(
        Id customerId,
        CancellationToken cancellationToken = default) =>
        context.Shortlists
            // Tracked, and with the entries: every caller of this either adds one or removes one,
            // and the cap is an invariant over the loaded set.
            .Include("_entries")
            .FirstOrDefaultAsync(shortlist => shortlist.Id == customerId, cancellationToken);

    public void Add(CustomerShortlist shortlist) => context.Shortlists.Add(shortlist);

    public async Task<IReadOnlySet<Id>> SavedAmongAsync(
        Id customerId,
        IReadOnlyCollection<Id> vehicleIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vehicleIds);
        if (vehicleIds.Count == 0) return new HashSet<Id>();

        // Materialised into a List first: EF translates Contains over a local list, not over an
        // IReadOnlyCollection it cannot recognise as a parameter. The same note is on
        // `BookingReader.ForTab`, for the same reason.
        var wanted = vehicleIds.ToList();

        // Straight at the child table through the owner column, not through the aggregate: this
        // answers a question about a handful of named ids and has no business loading a list that
        // could hold fifty.
        var saved = await context.Set<ShortlistEntry>()
            .AsNoTracking()
            .Where(entry => entry.ShortlistId == customerId && wanted.Contains(entry.VehicleId))
            .Select(entry => entry.VehicleId)
            .ToListAsync(cancellationToken);

        return saved.ToHashSet();
    }
}
