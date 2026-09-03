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
public sealed record UpdateDeliverySettingsCommand(Id ActorUserId, bool IsEnabled, decimal RadiusKm)
    : ICommand<Result<DealerProfileDto, Error>>;

public sealed class UpdateDeliverySettingsCommandValidator : AbstractValidator<UpdateDeliverySettingsCommand>
{
    public UpdateDeliverySettingsCommandValidator()
    {
        RuleFor(command => command.RadiusKm)
            .InclusiveBetween(0m, DeliverySettings.MaxRadiusKm);
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
            var enabled = dealer.EnableDelivery(request.RadiusKm, now);
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
