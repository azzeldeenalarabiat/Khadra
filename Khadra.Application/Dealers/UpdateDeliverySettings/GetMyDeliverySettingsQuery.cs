using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using MediatR;

namespace Khadra.Application.Dealers.UpdateDeliverySettings;

/// <summary>
/// The delivery page (spec 4.4): whether this gallery delivers, how far it will drive, and what it
/// charges to do it — all three now the gallery's own.
/// </summary>
/// <param name="Fee">
/// Null exactly when delivery is off. There is no platform figure standing behind it any more, so
/// the screen has nothing to show and says so rather than displaying a zero nobody chose.
/// </param>
/// <param name="MaxFee">
/// The ceiling the domain enforces, sent so the form can refuse the same amounts the server would
/// and state the bound without repeating it as a literal.
/// </param>
public sealed record DeliverySettingsViewDto(
    bool IsEnabled,
    decimal RadiusKm,
    decimal MaxRadiusKm,
    MoneyDto? Fee,
    decimal MaxFee,
    string CurrencyCode);

public sealed record GetMyDeliverySettingsQuery(Id UserId) : IQuery<Result<DeliverySettingsViewDto, Error>>;

public sealed class GetMyDeliverySettingsHandler(DealerMembershipResolver membership)
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

        // No IBusinessRulesProvider here any longer: this screen used to read the platform's fee
        // from it, and the whole answer now comes from the dealership itself.
        var delivery = member.Value.Dealer.Delivery;
        return new DeliverySettingsViewDto(
            delivery.IsEnabled,
            delivery.RadiusKm,
            DeliverySettings.MaxRadiusKm,
            MoneyDto.FromOptional(delivery.Fee),
            DeliverySettings.MaxFee,
            Money.JordanianDinar);
    }
}
