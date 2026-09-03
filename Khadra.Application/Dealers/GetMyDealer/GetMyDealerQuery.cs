using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Dealers.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using MediatR;

namespace Khadra.Application.Dealers.GetMyDealer;

/// <summary>
/// The dealer the signed-in user belongs to, whether as its owner or as an employee.
///
/// Deliberately readable while PENDING_REVIEW: an applicant has to be able to see that they are
/// waiting, what they submitted, and any clarification note an Admin sent back. It is the trading
/// actions that are gated, not the ability to look at your own application.
/// </summary>
public sealed record GetMyDealerQuery(Id UserId) : IQuery<Result<DealerProfileDto, Error>>;

public sealed class GetMyDealerHandler(IDealerRepository dealers)
    : IRequestHandler<GetMyDealerQuery, Result<DealerProfileDto, Error>>
{
    public async Task<Result<DealerProfileDto, Error>> Handle(
        GetMyDealerQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dealer = await dealers.GetByOwnerUserIdAsync(request.UserId, cancellationToken)
            ?? await dealers.GetByStaffUserIdAsync(request.UserId, cancellationToken);

        return dealer is null ? DealerErrors.NotRegistered : DealerProfileDto.From(dealer);
    }
}
