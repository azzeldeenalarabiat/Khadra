using Khadra.Application.Common;
using Khadra.Domain.Common;

namespace Khadra.Application.Bookings.ReadModels;

// Raw booking FACTS for one dealership's console. Instants, amounts and ids only; the handler does the
// calendar bucketing (IReportingCalendar) and the money arithmetic (Money/Percentage), because the
// database knows neither the Amman calendar nor how a Percentage rounds.

/// <summary>Headcounts the dashboard leads with.</summary>
public sealed record DealerBookingCounts(
    int Requested,
    DateTimeOffset? OldestRequestedAt,
    int Approved,
    int PickedUp,
    int OverdueReturns);

/// <summary>A pickup or a return the dealer has coming.</summary>
public sealed record UpcomingHandover(
    Guid BookingId,
    string Reference,
    string Status,
    DateTimeOffset When,
    string PickupMethod,
    Guid VehicleId,
    string CustomerName,
    bool IsOverdue);

/// <summary>One booking's contribution to revenue, at the rate FROZEN on that booking.</summary>
public sealed record RevenueFact(
    Guid BookingId,
    string Status,
    DateTimeOffset EarnedAt,
    decimal RentalTotal,
    string Currency,
    decimal CommissionPercent);

/// <summary>The stretch of a rental that overlaps a reporting window, for occupancy.</summary>
public sealed record OccupancyFact(Guid VehicleId, DateTimeOffset Start, DateTimeOffset End, string Status);

/// <summary>Something a member of staff did to a booking, from the booking's own history.</summary>
public sealed record DealerActivityEntry(
    Guid BookingId,
    string Reference,
    string ToStatus,
    string? FromStatus,
    Guid? ActorUserId,
    string ActorName,
    string? Reason,
    DateTimeOffset OccurredAt);

public interface IDealerBookingReader
{
    Task<DealerBookingCounts> CountsAsync(Id dealerId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Approved bookings starting inside [from, to).</summary>
    Task<IReadOnlyList<UpcomingHandover>> UpcomingPickupsAsync(Id dealerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Cars out on rental due back inside [from, to), plus any already overdue.</summary>
    Task<IReadOnlyList<UpcomingHandover>> UpcomingReturnsAsync(Id dealerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Vehicles this dealer's live bookings hold right now.</summary>
    Task<IReadOnlyList<Guid>> HeldVehicleIdsAsync(Id dealerId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Returned and Completed bookings whose car came back inside [from, to); PickedUp ones are "in progress".</summary>
    Task<IReadOnlyList<RevenueFact>> RevenueAsync(Id dealerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Rentals (PickedUp, Returned, Completed) whose period overlaps [from, to).</summary>
    Task<IReadOnlyList<OccupancyFact>> OccupancyAsync(Id dealerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>The dealership's trail, or one person's within it when <paramref name="actorUserId"/> is given.</summary>
    Task<PagedResult<DealerActivityEntry>> ActivityAsync(Id dealerId, PageRequest page, Id? actorUserId = null, CancellationToken cancellationToken = default);
}
