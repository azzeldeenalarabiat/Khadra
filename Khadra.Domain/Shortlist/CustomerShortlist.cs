using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Entity = Khadra.Domain.Common.Entity;

namespace Khadra.Domain.Shortlist;

/// <summary>
/// The cars one customer has saved to look at again.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own bounded context, and neither Fleet nor IdentityAccess.</b> Fleet is the dealer's
/// inventory, and its customer-facing side is an ANONYMOUS read model whose own remarks warn against
/// sharing types with dealer DTOs — a per-customer flag on the catalogue is exactly the leak that
/// warning is about, and it would teach Fleet what a customer is. IdentityAccess holds
/// <c>CustomerDocument</c> because that is identity verification; a shopping preference is not.
/// </para>
/// <para>
/// <b>One aggregate per customer, keyed BY the customer.</b> The id is the customer's own, which
/// makes "does this person have a shortlist" a primary-key lookup and makes two concurrent first
/// saves collide on the key rather than create two lists. The entries are children, so the cap is an
/// invariant of one loaded object rather than a count somebody has to remember to run.
/// </para>
/// <para>
/// <b>Cross-context references by id only.</b> There is no navigation to <c>Vehicle</c>, and the
/// reader left-joins: a saved car can legitimately become invisible — hidden, in maintenance, its
/// gallery suspended, soft-deleted — and the entry must survive all of it. Maintenance → Hidden →
/// Active is a normal round trip for a gallery, and an entry auto-removed on the way through would
/// be a customer's list quietly editing itself.
/// </para>
/// <para>
/// <b>Both mutations are idempotent.</b> Saving what is already saved succeeds, and removing what is
/// not there succeeds. A heart is a toggle on a Jordanian mobile network, and a retried tap must not
/// be an error the customer has to understand.
/// </para>
/// </remarks>
public sealed class CustomerShortlist : AggregateRoot
{
    private readonly List<ShortlistEntry> _entries = [];

    private CustomerShortlist()
    {
    }

    private CustomerShortlist(Id customerId, DateTimeOffset now) : base(customerId)
    {
        CreatedAt = now;
    }

    /// <summary>The customer, which is also this aggregate's identity.</summary>
    public Id CustomerId => Id;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Newest first, which is the order a saved list is read in.</summary>
    public IReadOnlyList<ShortlistEntry> Entries =>
        _entries.OrderByDescending(entry => entry.SavedAt).ThenByDescending(entry => entry.Id).ToList();

    public int Count => _entries.Count;

    /// <summary>Starts a list. Called on the first save and never before.</summary>
    /// <remarks>
    /// An empty shortlist is not created at registration: a row per account for a feature most of
    /// them will not use is a table that grows with the user base and says nothing.
    /// </remarks>
    public static CustomerShortlist Start(Id customerId, DateTimeOffset now) =>
        new(customerId, now);

    public bool Contains(Id vehicleId) => _entries.Exists(entry => entry.VehicleId == vehicleId);

    /// <summary>
    /// Saves a car, or does nothing because it is already saved.
    /// </summary>
    /// <param name="maximumEntries">
    /// The platform's cap, passed in rather than read here: business numbers come from
    /// <c>IBusinessRulesProvider</c> and a domain object does not reach for configuration.
    /// </param>
    public UnitResult<Error> Add(Id vehicleId, int maximumEntries, DateTimeOffset now)
    {
        // Idempotent, and checked BEFORE the cap: a full list must not refuse to re-save something
        // already on it, which is what a retried tap on the last slot looks like.
        if (Contains(vehicleId))
            return UnitResult.Success<Error>();

        if (_entries.Count >= maximumEntries)
            return ShortlistErrors.Full(maximumEntries);

        _entries.Add(ShortlistEntry.Create(Id, vehicleId, now));
        return UnitResult.Success<Error>();
    }

    /// <summary>Removes a car, or does nothing because it was not saved.</summary>
    public void Remove(Id vehicleId) => _entries.RemoveAll(entry => entry.VehicleId == vehicleId);
}

/// <summary>One saved car.</summary>
/// <remarks>
/// It carries an id and the moment it was saved, and nothing else — no make, no model, no price. A
/// snapshot of those would go stale the first time a gallery corrected a listing, and a screen
/// rendering last month's price beside today's car is worse than a screen rendering neither.
///
/// The saved list DOES name a car it can no longer offer, but it reads that name live from the
/// vehicle when the list is read (<c>IShortlistReader</c>), which is why there is still nothing to
/// snapshot here.
/// </remarks>
public sealed class ShortlistEntry : Entity
{
    private ShortlistEntry()
    {
    }

    private ShortlistEntry(Id id, Id shortlistId, Id vehicleId, DateTimeOffset savedAt) : base(id)
    {
        ShortlistId = shortlistId;
        VehicleId = vehicleId;
        SavedAt = savedAt;
    }

    /// <summary>Whose list this is on — which, this aggregate being keyed by customer, is also them.</summary>
    /// <remarks>
    /// A real property rather than a shadow foreign key, matching <c>DisputeStatement.TicketId</c>:
    /// every read of this table filters by it, and a shadow property makes those reads spell the
    /// column name as a string.
    /// </remarks>
    public Id ShortlistId { get; private set; }

    public Id VehicleId { get; private set; }
    public DateTimeOffset SavedAt { get; private set; }

    internal static ShortlistEntry Create(Id shortlistId, Id vehicleId, DateTimeOffset savedAt) =>
        new(Id.New(), shortlistId, vehicleId, savedAt);
}
