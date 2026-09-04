using CSharpFunctionalExtensions;
using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.AdminDashboard.GetAdminWorkload;

/// <summary>
/// How much work is sitting with the platform right now: two numbers, nothing else.
///
/// This is the one request every admin screen makes, so it is the one that has to be cheap. It is
/// two single-row aggregates over the two smallest tables. The console badges the navigation with it,
/// but the contract is named for the FACT and not the drawing — a topbar count, a digest email or an
/// admin app would all ask the same question, and "nav badges" would have baked one screen's layout
/// into a URL nobody could rename afterwards.
///
/// It exists because the figures have to be CURRENT. They used to come from the whole-dashboard
/// snapshot, which was fetched once when the shell first loaded and reloaded only by the dashboard's
/// own retry button — so approving a dealer left the badge claiming that dealer still needed
/// reviewing, for the rest of the session. Small enough to re-fetch on every navigation and after
/// every decision, which is what makes it true rather than merely live-looking.
/// </summary>
public sealed record GetAdminWorkloadQuery : IQuery<Result<AdminWorkloadDto, Error>>;

public sealed class GetAdminWorkloadHandler(
    IDealerDashboardReader dealers,
    IDisputeDashboardReader disputes,
    IClock clock)
    : IRequestHandler<GetAdminWorkloadQuery, Result<AdminWorkloadDto, Error>>
{
    public async Task<Result<AdminWorkloadDto, Error>> Handle(
        GetAdminWorkloadQuery request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var dealerCounts = await dealers.CountsAsync(cancellationToken);
        // The resolved-window figures are not wanted here, so the window is the narrowest that still
        // satisfies the reader; Open and UnderReview are the only counts read.
        var disputeCounts = await disputes.CountsAsync(now, now, cancellationToken);

        return new AdminWorkloadDto(
            now,
            // PendingReview only -- the domain's own DealerVerificationStatus.IsAwaitingAdmin.
            // ClarificationNeeded was counted here too, which said four applications needed an admin
            // when two did, and disagreed on screen with the "Pending review 2" card beside it. An
            // application sent back for clarification is waiting on the DEALER; it is not the
            // platform's move, and a badge that counts it sends an admin to a queue with nothing to do.
            dealerCounts.PendingReview,
            disputeCounts.Open + disputeCounts.UnderReview);
    }
}
