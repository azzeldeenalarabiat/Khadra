namespace Khadra.Application.Common.Ports;

/// <summary>
/// Serialises the create-a-booking sequence for ONE vehicle, for the life of the caller's transaction.
/// </summary>
/// <remarks>
/// Creating a booking is a check-then-act: expire the holds whose clock has run out, ask whether
/// anything still holds the car, then insert. The exclusion constraint is the floor under that, and
/// it is enough to stop two customers ever holding one car — but it is not enough to make the ANSWER
/// truthful, because the expiry step writes to rows a concurrent request is also writing to.
///
/// Without serialisation, two customers asking for the same car on DIFFERENT weeks can both load the
/// same expired hold; one wins on <c>xmin</c> and the other is refused, having lost a race over
/// dates that were free. Whatever refusal that produced would be a lie.
///
/// Taking a lock per vehicle removes the case rather than reporting it: the second request waits,
/// finds the first one's work already committed, and answers from a world that has stopped moving.
/// One vehicle per booking means there is no lock ordering to get wrong and no deadlock to have.
///
/// The lock is scoped to the transaction and released when it ends, so a caller cannot leak one, and
/// a connection returned to the pool never carries it. Outside a transaction it does nothing useful,
/// so it must only be called inside one.
/// </remarks>
public interface IVehicleHoldLock
{
    /// <summary>
    /// Waits until this transaction is the only one creating a booking for <paramref name="vehicleId"/>.
    /// </summary>
    Task AcquireAsync(Guid vehicleId, CancellationToken cancellationToken = default);
}
