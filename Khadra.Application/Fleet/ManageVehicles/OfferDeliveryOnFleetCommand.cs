using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Fleet.Repositories;
using MediatR;

namespace Khadra.Application.Fleet.ManageVehicles;

/// <summary>How many cars the action changed, so the screen can say a number rather than "done".</summary>
public sealed record FleetDeliveryResult(int Updated);

/// <summary>
/// Offers delivery on every car this gallery already has listed.
/// </summary>
/// <remarks>
/// <para>
/// Closes pre-launch item 75. A vehicle carries its own <c>IsDeliveryEligible</c>, and the wizard sets
/// it from whether the dealership offered delivery AT THE MOMENT THE CAR WAS SAVED. A gallery that
/// lists its fleet first and turns delivery on afterwards therefore advertises a service none of its
/// cars can provide, and nothing on any screen connected the two facts.
/// </para>
/// <para>
/// <b>The per-car flag stays, and this does not weaken it.</b> An office with one van it will not
/// drive across Amman needs to be able to say so, and still can: this is a bulk EDIT a human asks
/// for, on a page that tells them how many cars it will touch — not a rule keeping the flag in step.
/// Nothing here fires automatically.
/// </para>
/// <para>
/// There is deliberately no bulk action the other way. A gallery switching delivery off keeps its
/// per-car answers, because clearing them would mean that turning delivery back on later silently
/// re-offered the van its owner had excluded on purpose.
/// </para>
/// <para>
/// Only ACTIVE cars. A draft is not advertising anything and is not part of the mismatch; editing one
/// would be changing a listing its owner has not finished writing.
/// </para>
/// </remarks>
public sealed record OfferDeliveryOnListedVehiclesCommand(Id ActorUserId)
    : ICommand<Result<FleetDeliveryResult, Error>>;

public sealed class OfferDeliveryOnListedVehiclesHandler(
    IDealerRepository dealers,
    IVehicleRepository vehicles,
    IUnitOfWork unitOfWork)
    : IRequestHandler<OfferDeliveryOnListedVehiclesCommand, Result<FleetDeliveryResult, Error>>
{
    public async Task<Result<FleetDeliveryResult, Error>> Handle(
        OfferDeliveryOnListedVehiclesCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // By OWNER, like every other change to what the business offers. The lookup itself is the
        // authorization: an employee simply has no dealership by this route, and the answer they get
        // is the same one somebody with no dealership at all gets.
        var dealer = await dealers.GetByOwnerUserIdAsync(request.ActorUserId, cancellationToken);
        if (dealer is null)
            return DealerErrors.NotRegistered;

        // A pending, rejected or suspended dealership stops here (spec 3.1).
        var canTrade = dealer.EnsureCanTrade();
        if (canTrade.IsFailure)
            return canTrade.Error;

        // A gallery that does not deliver cannot offer delivery on its cars. Refused rather than
        // quietly doing nothing: the screen cannot reach this state, and if it ever does, the answer
        // that helps is "switch delivery on first".
        if (!dealer.Delivery.IsEnabled)
            return DealerErrors.DeliveryNotOffered;

        var pending = await vehicles.ListPublishedNotDeliveryEligibleAsync(dealer.Id, cancellationToken);
        if (pending.Count == 0)
            return new FleetDeliveryResult(0);

        foreach (var vehicle in pending)
            vehicle.SetDeliveryEligible(true);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new FleetDeliveryResult(pending.Count);
    }
}
