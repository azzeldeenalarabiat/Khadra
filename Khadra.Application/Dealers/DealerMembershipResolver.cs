using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;

namespace Khadra.Application.Dealers;

/// <summary>Who the signed-in person is to a dealership: its owner, or one of its ACTIVE employees.</summary>
public sealed record DealerMembership(Dealer Dealer, bool IsOwner, Employee? Employee)
{
    /// <summary>Whether this person may approve or reject bookings: owner or active employee, AND the business may trade.</summary>
    public bool CanActOnBookings => Dealer.CanActOnBookings(IsOwner ? Dealer.OwnerUserId : Employee!.UserId);

    public bool CanViewReports => IsOwner || Employee!.CanViewReports;
}

/// <summary>
/// "Which dealership does this user belong to, and do they still have standing there?" -- answered
/// once, for every dealer-staff handler and for the authorization pipeline.
///
/// The question has a trap that this class exists to close. IDealerRepository.GetByStaffUserIdAsync
/// matches ANY employee row, active or not, because a deactivated employee must stay loadable (their
/// booking history names them, and re-hiring reactivates the same identity). Every caller that used
/// that lookup directly was therefore treating a deactivated employee as staff: listing them the
/// dealer's whole booking book, passing them through the approved-dealer gate. Spec 4.2 says their
/// access ends immediately. Here, a deactivated employee is simply not a member.
///
/// Scoped, and memoised: the authorization handler and the use-case handler ask the same question in
/// the same request, and the dealer aggregate is not small.
/// </summary>
public sealed class DealerMembershipResolver(IDealerRepository dealers)
{
    private Id? _resolvedFor;
    private Result<DealerMembership, Error> _resolved;

    public async Task<Result<DealerMembership, Error>> ResolveAsync(Id userId, CancellationToken cancellationToken = default)
    {
        if (_resolvedFor == userId)
            return _resolved;

        var dealer = await dealers.GetByOwnerUserIdAsync(userId, cancellationToken)
            ?? await dealers.GetByStaffUserIdAsync(userId, cancellationToken);

        _resolvedFor = userId;
        _resolved = Resolve(dealer, userId);
        return _resolved;
    }

    private static Result<DealerMembership, Error> Resolve(Dealer? dealer, Id userId)
    {
        if (dealer is null)
            return DealerErrors.NotRegistered;

        if (dealer.OwnerUserId == userId)
            return new DealerMembership(dealer, IsOwner: true, Employee: null);

        var employee = dealer.Employees.SingleOrDefault(candidate => candidate.UserId == userId);
        // A deactivated employee has no standing: the same answer as never having been staff, so the
        // console shows them the "no dealership" state rather than a half-working one.
        if (employee is null || !employee.IsActive)
            return DealerErrors.NotRegistered;

        return new DealerMembership(dealer, IsOwner: false, employee);
    }
}
