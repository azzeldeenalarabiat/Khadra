namespace Khadra.Application.Common;

/// <summary>
/// One page of a list, with what a client needs to render a pager.
///
/// TotalCount is included rather than left to the client to infer: the console shows "Showing 8 of
/// 148 dealers", and a client that only knows it received a full page cannot tell the difference
/// between the last page and a page that happens to be full.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;
}

/// <summary>Non-generic entry point for the empty page, so the generic type carries no statics.</summary>
public static class PagedResult
{
    public static PagedResult<T> Empty<T>(int page, int pageSize) => new([], page, pageSize, 0);
}

/// <summary>
/// Paging as it arrives from a query string, clamped.
///
/// The clamp is the point: an unbounded page size is a denial-of-service handed to any caller, and a
/// zero or negative page is an off-by-one waiting to happen.
/// </summary>
public sealed record PageRequest(int Page, int PageSize)
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public static PageRequest From(int? page, int? pageSize) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));

    public int Skip => (Page - 1) * PageSize;
}
