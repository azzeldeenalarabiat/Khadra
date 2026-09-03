using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

// Write-side repository: every read here loads the WHOLE aggregate, employees and documents included,
// because a caller holding a Dealer is about to change it and the invariants (one employee row per
// person, all three documents before approval) are enforced across those children. Dashboard counts
// deliberately do not come through here -- that is what the reader ports are for.
internal sealed class DealerRepository(KhadraDbContext context) : IDealerRepository
{
    public Task<Dealer?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        WithChildren().SingleOrDefaultAsync(dealer => dealer.Id == id, cancellationToken);

    public Task<Dealer?> GetByOwnerUserIdAsync(Id ownerUserId, CancellationToken cancellationToken = default) =>
        WithChildren().SingleOrDefaultAsync(dealer => dealer.OwnerUserId == ownerUserId, cancellationToken);

    public Task<Dealer?> GetByStaffUserIdAsync(Id userId, CancellationToken cancellationToken = default) =>
        WithChildren().SingleOrDefaultAsync(
            dealer => dealer.Employees.Any(employee => employee.UserId == userId),
            cancellationToken);

    public Task<bool> ExistsForOwnerAsync(Id ownerUserId, CancellationToken cancellationToken = default) =>
        context.Dealers.AnyAsync(dealer => dealer.OwnerUserId == ownerUserId, cancellationToken);

    public Task<bool> CommercialRegistrationExistsAsync(
        CommercialRegistrationNumber number,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(number);
        return context.Dealers.AnyAsync(dealer => dealer.CommercialRegistration == number, cancellationToken);
    }

    public async Task<IReadOnlyList<Dealer>> ListAwaitingReviewAsync(CancellationToken cancellationToken = default)
    {
        var pending = DealerVerificationStatus.PendingReview;
        return await WithChildren()
            .Where(dealer => dealer.VerificationStatus == pending)
            .OrderBy(dealer => dealer.ReviewDueAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Dealer dealer, CancellationToken cancellationToken = default) =>
        await context.Dealers.AddAsync(dealer, cancellationToken);

    private IQueryable<Dealer> WithChildren() =>
        context.Dealers
            .Include(dealer => dealer.Employees)
            .Include(dealer => dealer.Documents);
}
