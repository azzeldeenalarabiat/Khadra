using CSharpFunctionalExtensions;
using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.AdminDashboard.GetCustomerCounts;

/// <summary>The customer KPI card: how many people can rent, and how many are waiting to be let in.</summary>
public sealed record GetCustomerCountsQuery : IQuery<Result<CustomerCountsDto, Error>>;

public sealed class GetCustomerCountsHandler(ICustomerDashboardReader customers, IClock clock)
    : IRequestHandler<GetCustomerCountsQuery, Result<CustomerCountsDto, Error>>
{
    public async Task<Result<CustomerCountsDto, Error>> Handle(
        GetCustomerCountsQuery request,
        CancellationToken cancellationToken)
    {
        var counts = await customers.CountsAsync(cancellationToken);

        return new CustomerCountsDto(
            clock.UtcNow,
            counts.Total,
            counts.Verified,
            counts.PendingVerification,
            counts.Suspended);
    }
}
