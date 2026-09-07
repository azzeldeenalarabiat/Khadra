using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.AdminCustomers;

// The people who rent (spec 5). An administrator can read them and stop one trading; everything else
// about an account stays the account holder's own.

public sealed record ListCustomersQuery(string? Status, string? Search, bool? UnverifiedOnly, PageRequest Page)
    : IQuery<Result<PagedResult<CustomerListItem>, Error>>;

public sealed record GetCustomerQuery(Id UserId) : IQuery<Result<CustomerProfile, Error>>;

public sealed record GetCustomerCountsQuery : IQuery<Result<CustomerCountsView, Error>>;

/// <summary>Stops a customer using the platform. Their sessions end with the rotated security stamp.</summary>
public sealed record SuspendCustomerCommand(Id UserId, string Reason) : ICommand<Result<CustomerProfile, Error>>;

public sealed record ReactivateCustomerCommand(Id UserId) : ICommand<Result<CustomerProfile, Error>>;

public sealed class SuspendCustomerCommandValidator : AbstractValidator<SuspendCustomerCommand>
{
    public SuspendCustomerCommandValidator()
    {
        // 500, not 1000: it is the width of users.suspension_reason. Accepting more here would turn
        // a long reason into a DbUpdateException at commit, reported as a conflict nobody caused.
        RuleFor(command => command.Reason)
            .NotEmpty()
            .WithMessage("A reason is required: it is recorded against the account and in the audit log.")
            .MaximumLength(500);
    }
}

public sealed class ListCustomersQueryValidator : AbstractValidator<ListCustomersQuery>
{
    public ListCustomersQueryValidator()
    {
        RuleFor(query => query.Status)
            .Must(status => status is null ||
                            Enumeration.GetAll<UserStatus>().Any(candidate =>
                                string.Equals(candidate.Name, status, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Unknown account status.");
        RuleFor(query => query.Search).MaximumLength(200);
    }
}

public sealed class AdminCustomerQueryHandlers(ICustomerAdminReader reader) :
    IRequestHandler<ListCustomersQuery, Result<PagedResult<CustomerListItem>, Error>>,
    IRequestHandler<GetCustomerQuery, Result<CustomerProfile, Error>>,
    IRequestHandler<GetCustomerCountsQuery, Result<CustomerCountsView, Error>>
{
    public async Task<Result<PagedResult<CustomerListItem>, Error>> Handle(
        ListCustomersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await reader.ListAsync(
            new CustomerListFilter(request.Status, request.Search, request.UnverifiedOnly),
            request.Page,
            cancellationToken);
    }

    public async Task<Result<CustomerProfile, Error>> Handle(GetCustomerQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = await reader.GetAsync(request.UserId, cancellationToken);
        // The reader already refuses a non-customer, so an admin probing this route with a dealer's
        // id gets the same answer as for an id that does not exist.
        return profile is null ? IdentityErrors.UserNotFound : profile;
    }

    public async Task<Result<CustomerCountsView, Error>> Handle(GetCustomerCountsQuery request, CancellationToken cancellationToken)
    {
        return await reader.CountsAsync(cancellationToken);
    }
}

public sealed class AdminCustomerCommandHandlers(
    IUserRepository users,
    ICustomerAdminReader reader,
    AdminActionRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock) :
    IRequestHandler<SuspendCustomerCommand, Result<CustomerProfile, Error>>,
    IRequestHandler<ReactivateCustomerCommand, Result<CustomerProfile, Error>>
{
    public Task<Result<CustomerProfile, Error>> Handle(SuspendCustomerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ActAsync(
            request.UserId,
            (user, now) => user.Suspend(request.Reason, now),
            AuditAction.CustomerSuspended,
            request.Reason,
            cancellationToken);
    }

    public Task<Result<CustomerProfile, Error>> Handle(ReactivateCustomerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ActAsync(
            request.UserId,
            (user, now) => user.Reactivate(now),
            AuditAction.CustomerReactivated,
            reason: null,
            cancellationToken);
    }

    private async Task<Result<CustomerProfile, Error>> ActAsync(
        Id userId,
        Func<User, DateTimeOffset, UnitResult<Error>> act,
        AuditAction action,
        string? reason,
        CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);

        // Not-found rather than forbidden for a user who is not a customer: this route speaks about
        // customers, and answering "wrong role" would confirm the id belongs to a dealer or an
        // administrator to anyone who guessed it. Suspending staff through here would also lock a
        // dealer owner out while their dealership went on trading, under the wrong audit action.
        if (user is null || user.Role != UserRole.Customer)
            return IdentityErrors.UserNotFound;

        var previousStatus = user.Status.Name;
        var outcome = act(user, clock.UtcNow);
        if (outcome.IsFailure)
            return outcome.Error;

        // The label is a REFERENCE, never the customer's name, email or phone. `audit_entries` is
        // append-only by trigger and by guard, so identity written into it can never be taken back
        // out; the entity id already says exactly who this is to anyone entitled to look.
        audit.Record(
            action,
            AuditEntityType.Customer,
            user.Id,
            $"Customer {user.Id.Value:N}"[..17],
            previousStatus,
            user.Status.Name,
            reason);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var profile = await reader.GetAsync(user.Id, cancellationToken);
        return profile is null ? IdentityErrors.UserNotFound : profile;
    }
}
