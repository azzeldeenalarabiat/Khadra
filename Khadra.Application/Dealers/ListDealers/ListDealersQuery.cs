using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.Dealers.ListDealers;

/// <summary>
/// The Admin's dealer list (spec 3.1 and 3.2).
///
/// A read model rather than the repository: the screen shows twenty rows of name, status and counts,
/// and loading twenty whole aggregates with their employees and documents to render that would be
/// wasteful in a way that only shows up once there are real dealers.
/// </summary>
public sealed record ListDealersQuery(
    string? Status,
    bool? SuspendedOnly,
    string? Search,
    int? Page,
    int? PageSize) : IQuery<Result<PagedResult<DealerListItem>, Error>>;

public sealed class ListDealersHandler(IDealerAdminReader dealers)
    : IRequestHandler<ListDealersQuery, Result<PagedResult<DealerListItem>, Error>>
{
    public async Task<Result<PagedResult<DealerListItem>, Error>> Handle(
        ListDealersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = PageRequest.From(request.Page, request.PageSize);
        var filter = new DealerListFilter(request.Status, request.SuspendedOnly, request.Search);

        return await dealers.ListAsync(filter, page, cancellationToken);
    }
}
