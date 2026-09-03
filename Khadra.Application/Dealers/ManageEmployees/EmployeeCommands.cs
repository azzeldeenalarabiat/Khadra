using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Application.IdentityAccess;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.Dealers.ManageEmployees;

// Spec 4.2, the owner's staff: invite, re-invite, grant or withdraw report access, deactivate,
// reactivate, list. Every command is owner-only -- employees cannot manage employees -- and the
// pipeline policy (ApprovedDealer) has already required the business to be able to trade.

public sealed record InviteEmployeeCommand(Id OwnerUserId, string FullName, string Email, string Phone, bool CanViewReports)
    : ICommand<Result<EmployeeListItem, Error>>;

public sealed record ResendEmployeeInvitationCommand(Id OwnerUserId, Id EmployeeId) : ICommand<Result<EmployeeListItem, Error>>;

public sealed record SetEmployeeReportAccessCommand(Id OwnerUserId, Id EmployeeId, bool CanViewReports)
    : ICommand<Result<EmployeeListItem, Error>>;

public sealed record DeactivateEmployeeCommand(Id OwnerUserId, Id EmployeeId) : ICommand<Result<EmployeeListItem, Error>>;

public sealed record ReactivateEmployeeCommand(Id OwnerUserId, Id EmployeeId) : ICommand<Result<EmployeeListItem, Error>>;

public sealed record ListMyEmployeesQuery(Id OwnerUserId) : IQuery<Result<IReadOnlyList<EmployeeListItem>, Error>>;

public sealed class InviteEmployeeCommandValidator : AbstractValidator<InviteEmployeeCommand>
{
    public InviteEmployeeCommandValidator()
    {
        RuleFor(command => command.FullName).NotEmpty().MaximumLength(150);
        RuleFor(command => command.Email).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Phone).NotEmpty().MaximumLength(20);
    }
}

public sealed class EmployeeHandlers(
    DealerMembershipResolver membership,
    EmployeeAccountProvisioner provisioner,
    IUserRepository users,
    IEmployeeReader reader,
    AuthEmailDispatcher emails,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<InviteEmployeeCommand, Result<EmployeeListItem, Error>>,
    IRequestHandler<ResendEmployeeInvitationCommand, Result<EmployeeListItem, Error>>,
    IRequestHandler<SetEmployeeReportAccessCommand, Result<EmployeeListItem, Error>>,
    IRequestHandler<DeactivateEmployeeCommand, Result<EmployeeListItem, Error>>,
    IRequestHandler<ReactivateEmployeeCommand, Result<EmployeeListItem, Error>>,
    IRequestHandler<ListMyEmployeesQuery, Result<IReadOnlyList<EmployeeListItem>, Error>>
{
    public async Task<Result<EmployeeListItem, Error>> Handle(InviteEmployeeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await OwnedAsync(request.OwnerUserId, cancellationToken);
        if (owned.IsFailure)
            return owned.Error;
        var dealer = owned.Value;

        // The Identity half: validated, unique, inert until accepted. Staged, not saved.
        var provisioned = await provisioner.ProvisionAsync(request.Email, request.Phone, request.FullName, cancellationToken);
        if (provisioned.IsFailure)
            return provisioned.Error;

        // The Dealers half. One SaveChangesAsync commits both aggregates and the token together, so
        // an employee cannot exist in one context and not the other; deliberately not an outer
        // transaction, which would dispatch the domain events before commit.
        var hired = dealer.HireEmployee(provisioned.Value.User.Id, request.CanViewReports, clock.UtcNow);
        if (hired.IsFailure)
            return hired.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After commit, and never a reason to fail: the invitation can be re-sent from the list.
        await emails.SendEmployeeInvitationAsync(
            provisioned.Value.User, dealer.BusinessName.Value, provisioned.Value.RawInvitationToken, cancellationToken);

        return await ItemAsync(dealer.Id, hired.Value.Id, cancellationToken);
    }

    public async Task<Result<EmployeeListItem, Error>> Handle(ResendEmployeeInvitationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadAsync(request.OwnerUserId, request.EmployeeId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;
        var (dealer, employee, user) = loaded.Value;

        // "Invited" is precisely "has not proved the mailbox yet".
        if (user.IsEmailVerified)
            return DealerErrors.InvitationAlreadyAccepted;

        var rawToken = await provisioner.ReissueInvitationAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await emails.SendEmployeeInvitationAsync(user, dealer.BusinessName.Value, rawToken, cancellationToken);

        return await ItemAsync(dealer.Id, employee.Id, cancellationToken);
    }

    public async Task<Result<EmployeeListItem, Error>> Handle(SetEmployeeReportAccessCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadAsync(request.OwnerUserId, request.EmployeeId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;
        var (dealer, employee, _) = loaded.Value;

        var changed = dealer.SetEmployeeReportAccess(employee.Id, request.CanViewReports);
        if (changed.IsFailure)
            return changed.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ItemAsync(dealer.Id, employee.Id, cancellationToken);
    }

    public async Task<Result<EmployeeListItem, Error>> Handle(DeactivateEmployeeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadAsync(request.OwnerUserId, request.EmployeeId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;
        var (dealer, employee, user) = loaded.Value;

        var now = clock.UtcNow;
        var deactivated = dealer.DeactivateEmployee(employee.Id, now);
        if (deactivated.IsFailure)
            return deactivated.Error;

        // Spec 4.2: "their login stops working immediately". The membership flag alone would only
        // take effect on the next dealer route; rotating the stamp ends the sessions they hold now.
        user.RevokeAllSessions(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ItemAsync(dealer.Id, employee.Id, cancellationToken);
    }

    public async Task<Result<EmployeeListItem, Error>> Handle(ReactivateEmployeeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadAsync(request.OwnerUserId, request.EmployeeId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;
        var (dealer, employee, _) = loaded.Value;

        if (employee.IsActive)
            return DealerErrors.EmployeeAlreadyActive;

        var reactivated = dealer.ReactivateEmployee(employee.Id);
        if (reactivated.IsFailure)
            return reactivated.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ItemAsync(dealer.Id, employee.Id, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<EmployeeListItem>, Error>> Handle(ListMyEmployeesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await OwnedAsync(request.OwnerUserId, cancellationToken);
        if (owned.IsFailure)
            return owned.Error;

        return Result.Success<IReadOnlyList<EmployeeListItem>, Error>(
            await reader.ListAsync(owned.Value.Id, cancellationToken));
    }

    /// <summary>The caller's dealership, and only if they OWN it.</summary>
    private async Task<Result<Dealer, Error>> OwnedAsync(Id ownerUserId, CancellationToken cancellationToken)
    {
        var member = await membership.ResolveAsync(ownerUserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;

        return member.Value.IsOwner ? member.Value.Dealer : DealerErrors.OwnerOnly;
    }

    private async Task<Result<(Dealer Dealer, Employee Employee, User User), Error>> LoadAsync(
        Id ownerUserId,
        Id employeeId,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedAsync(ownerUserId, cancellationToken);
        if (owned.IsFailure)
            return owned.Error;

        var employee = owned.Value.Employees.SingleOrDefault(candidate => candidate.Id == employeeId);
        if (employee is null)
            return DealerErrors.EmployeeNotFound;

        var user = await users.GetByIdAsync(employee.UserId, cancellationToken);
        if (user is null)
            return DealerErrors.EmployeeNotFound;

        return (owned.Value, employee, user);
    }

    private async Task<Result<EmployeeListItem, Error>> ItemAsync(Id dealerId, Id employeeId, CancellationToken cancellationToken)
    {
        var item = await reader.GetAsync(dealerId, employeeId, cancellationToken);
        return item is null ? DealerErrors.EmployeeNotFound : item;
    }
}
