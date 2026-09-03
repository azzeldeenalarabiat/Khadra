using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using MediatR;

namespace Khadra.Application.Dealers.UpdateDeliverySettings;

/// <summary>
/// The delivery page (spec 4.4): the dealer's own switch and radius next to the fee the platform
/// charges for it. The fee is platform-wide and read from the business rules; it is shown so the
/// dealer knows what their customers will be asked to pay, and it is not theirs to edit.
/// </summary>
public sealed record DeliverySettingsViewDto(
    bool IsEnabled,
    decimal RadiusKm,
    decimal MaxRadiusKm,
    MoneyDto PlatformDeliveryFee);

public sealed record GetMyDeliverySettingsQuery(Id UserId) : IQuery<Result<DeliverySettingsViewDto, Error>>;

public sealed class GetMyDeliverySettingsHandler(
    DealerMembershipResolver membership,
    IBusinessRulesProvider businessRules)
    : IRequestHandler<GetMyDeliverySettingsQuery, Result<DeliverySettingsViewDto, Error>>
{
    public async Task<Result<DeliverySettingsViewDto, Error>> Handle(
        GetMyDeliverySettingsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var member = await membership.ResolveAsync(request.UserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;

        var rules = await businessRules.GetAsync(cancellationToken);
        var delivery = member.Value.Dealer.Delivery;
        return new DeliverySettingsViewDto(
            delivery.IsEnabled,
            delivery.RadiusKm,
            DeliverySettings.MaxRadiusKm,
            MoneyDto.From(rules.DeliveryFee));
    }
}
