using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings.Repositories;

public interface IHandoverCodeRepository
{
    Task AddAsync(HandoverCode code, CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest code issued for this booking and handover that has not been superseded — the one a
    /// presented code is checked against. It may be used, expired or locked; <see cref="HandoverCode.Verify"/>
    /// says which. Null when the customer never asked for one.
    /// </summary>
    Task<HandoverCode?> GetCurrentAsync(Id bookingId, HandoverType type, CancellationToken cancellationToken = default);

    /// <summary>Every code of this booking and handover that is still current, so a new one can replace them.</summary>
    Task<IReadOnlyList<HandoverCode>> ListCurrentAsync(Id bookingId, HandoverType type, CancellationToken cancellationToken = default);
}
