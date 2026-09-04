using CSharpFunctionalExtensions;
using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.AdminDashboard.GetAttentionQueue;

/// <summary>
/// The work queue: what the platform owes a decision on, soonest deadline first.
///
/// The heaviest panel on the dashboard, which is why it is its own request. It reads two contexts for
/// the rows and two more to label them, and it used to sit inside the composite where the KPI cards
/// waited on it. On its own it can also be refreshed at its own cadence — this is the panel an admin
/// actually watches, and the only one that needs to be current to the minute.
///
/// Every deadline is the one FROZEN on its record, never a fresh calculation against today's SLA:
/// raising the SLA must not retroactively breach a promise, nor lowering it forgive one.
/// </summary>
public sealed record GetAttentionQueueQuery : IQuery<Result<AttentionQueueDto, Error>>;

public sealed class GetAttentionQueueHandler(
    IDisputeDashboardReader disputes,
    IDealerDashboardReader dealers,
    IBookingDashboardReader bookings,
    IBusinessRulesProvider businessRules,
    IAdminDashboardSettings settings,
    IClock clock)
    : IRequestHandler<GetAttentionQueueQuery, Result<AttentionQueueDto, Error>>
{
    public async Task<Result<AttentionQueueDto, Error>> Handle(
        GetAttentionQueueQuery request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var rules = await businessRules.GetAsync(cancellationToken);

        // Sequential, and deliberately so: these readers share this request's scoped DbContext, and
        // EF throws the moment two operations overlap on one context. The parallelism this split buys
        // is ACROSS panels — each is its own request with its own scope — not inside one handler.
        var live = await disputes.LiveAsync(cancellationToken);
        var pending = await dealers.PendingApplicationsAsync(cancellationToken);
        var subtitles = await ResolveDisputeSubtitlesAsync(live, cancellationToken);

        return AttentionQueueBuilder.Build(
            live,
            subtitles,
            pending,
            settings.SlaWarningThreshold,
            rules.AdminSlaHours,
            now);
    }

    /// <summary>
    /// Builds "Aqaba Coast Cars · KH-20411" for each live ticket.
    ///
    /// A ticket knows only its BookingId, and a booking knows only its DealerId: cross-context
    /// references are by id, with no navigation properties. So the labels are resolved by asking each
    /// context in turn through its own port, rather than by joining three tables across three
    /// context boundaries in one query.
    /// </summary>
    private async Task<IReadOnlyDictionary<Id, string>> ResolveDisputeSubtitlesAsync(
        IReadOnlyList<LiveDispute> live,
        CancellationToken cancellationToken)
    {
        if (live.Count == 0)
            return new Dictionary<Id, string>();

        var labels = await bookings.LabelsAsync(
            [.. live.Select(dispute => dispute.BookingId).Distinct()],
            cancellationToken);
        var byBooking = labels.ToDictionary(label => label.BookingId);

        var names = await dealers.NamesAsync(
            [.. labels.Select(label => label.DealerId).Distinct()],
            cancellationToken);
        var byDealer = names.ToDictionary(name => name.DealerId, name => name.BusinessName);

        var subtitles = new Dictionary<Id, string>(live.Count);
        foreach (var dispute in live)
        {
            // A ticket whose booking has gone gets no subtitle rather than a fabricated one. The row
            // still renders: the deadline is the fact that matters, and inventing a label would be
            // the one thing this console must never do.
            if (!byBooking.TryGetValue(dispute.BookingId, out var label))
                continue;

            var dealerName = byDealer.GetValueOrDefault(label.DealerId);
            subtitles[dispute.TicketId] = dealerName is null
                ? label.Reference
                : $"{dealerName} · {label.Reference}";
        }

        return subtitles;
    }
}
