using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Dealers.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using MediatR;

namespace Khadra.Application.Dealers.UpdateDeliverySettings;

/// <summary>
/// Turns delivery on or off for a dealer (spec 4.4), and is the first action gated on approval.
///
/// The gate is <see cref="Dealer.EnsureCanTrade"/>, the domain's own answer to "may this business
/// operate": approved AND not suspended AND not deleted. Expressing it here rather than as an
/// authorization attribute means the caller gets a real ProblemDetails with
/// <c>dealer.not_approved</c> instead of a bare 403, and the rule can be unit-tested without an HTTP
/// pipeline. Every dealer-only action added later calls the same method.
/// </summary>
/// <param name="Fee">
/// What this gallery charges for a delivery, in JOD. Required when switching delivery on and
/// ignored when switching it off. The currency is never taken from the caller: there is one
/// currency on this platform and letting a client name it would be an invitation to price a
/// booking in something the rest of the system cannot add up.
/// </param>
public sealed record UpdateDeliverySettingsCommand(
    Id ActorUserId,
    bool IsEnabled,
    decimal RadiusKm,
    decimal? Fee) : ICommand<Result<DealerProfileDto, Error>>;

public sealed class UpdateDeliverySettingsCommandValidator : AbstractValidator<UpdateDeliverySettingsCommand>
{
    public UpdateDeliverySettingsCommandValidator()
    {
        RuleFor(command => command.RadiusKm)
            .InclusiveBetween(0m, DeliverySettings.MaxRadiusKm);
        // Only when delivery is being switched ON: the amount is meaningless otherwise, and
        // demanding one to switch delivery OFF would be a rule with no purpose.
        RuleFor(command => command.Fee)
            .NotNull()
            .When(command => command.IsEnabled)
            .WithMessage("A delivery fee is required to offer delivery.");
        RuleFor(command => command.Fee!.Value)
            .InclusiveBetween(0m, DeliverySettings.MaxFee)
            .When(command => command.IsEnabled && command.Fee is not null);
    }
}

public sealed class UpdateDeliverySettingsHandler(
    IDealerRepository dealers,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateDeliverySettingsCommand, Result<DealerProfileDto, Error>>
{
    public async Task<Result<DealerProfileDto, Error>> Handle(
        UpdateDeliverySettingsCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dealer = await dealers.GetByOwnerUserIdAsync(request.ActorUserId, cancellationToken);
        if (dealer is null)
            return DealerErrors.NotRegistered;

        // A PENDING_REVIEW, REJECTED or suspended dealer stops here (spec 3.1).
        var canTrade = dealer.EnsureCanTrade();
        if (canTrade.IsFailure)
            return canTrade.Error;

        var now = clock.UtcNow;
        if (request.IsEnabled)
        {
            // The validator has already refused a null fee here; the aggregate checks the bound
            // again, because it is the aggregate that owns what a delivery may cost.
            var enabled = dealer.EnableDelivery(
                request.RadiusKm, Money.Jod(request.Fee!.Value), now);
            if (enabled.IsFailure)
                return enabled.Error;
        }
        else
        {
            dealer.DisableDelivery(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DealerProfileDto.From(dealer);
    }
}
