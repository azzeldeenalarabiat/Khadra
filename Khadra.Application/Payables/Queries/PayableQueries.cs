using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Application.Payables.Dtos;
using Khadra.Application.Payables.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Payables;
using MediatR;

namespace Khadra.Application.Payables.Queries;

// ── The administrator's ledger ──────────────────────────────────────────────────────────────────────

/// <summary>Every office's balance per currency and kind of money, or one office's.</summary>
public sealed record GetOfficeBalancesQuery(Id? DealerId) : IQuery<IReadOnlyList<OfficeBalanceDto>>;

/// <param name="Scope">One of <see cref="PayableListScopes"/>.</param>
/// <param name="From">Only outcomes final on or after this Amman day.</param>
/// <param name="To">Only outcomes final on or before this Amman day.</param>
public sealed record ListOfficePayablesQuery(Id? DealerId, string Scope, DateOnly? From, DateOnly? To, int Page, int PageSize)
    : IQuery<PagedResult<OfficePayableDto>>;

/// <summary>The bookings a hold stops being recorded: their records need a person.</summary>
public sealed record ListPayableBookingHoldsQuery(Id? DealerId) : IQuery<IReadOnlyList<PayableHoldDto>>;

public sealed record ListOfficeSettlementsQuery(Id DealerId, int Page, int PageSize)
    : IQuery<Result<PagedResult<OfficeSettlementDto>, Error>>;

public sealed record GetOfficeSettlementQuery(Id SettlementId) : IQuery<Result<OfficeSettlementDetailDto, Error>>;

/// <summary>Khadra's own money over a span of Amman days; the current month when none is given.</summary>
public sealed record GetFinanceSummaryQuery(DateOnly? From, DateOnly? To) : IQuery<Result<FinanceSummaryDto, Error>>;

// ── The office's own payouts ────────────────────────────────────────────────────────────────────────

public sealed record GetMyPayoutsQuery(Id UserId) : IQuery<Result<OfficePayoutsDto, Error>>;

public sealed record ListMyPayablesQuery(Id UserId, string Scope, int Page, int PageSize)
    : IQuery<Result<PagedResult<OfficePayableDto>, Error>>;

public sealed record ListMySettlementsQuery(Id UserId, int Page, int PageSize)
    : IQuery<Result<PagedResult<OfficeSettlementDto>, Error>>;

public sealed record GetMySettlementQuery(Id UserId, Id SettlementId) : IQuery<Result<OfficeSettlementDetailDto, Error>>;

/// <summary>The administrator's reads of the office payables ledger (payments Phase 8). One reader, run one call at a time.</summary>
public sealed class AdminPayableQueryHandlers(IOfficeLedgerReader ledger, IReportingCalendar calendar, IClock clock)
    : IRequestHandler<GetOfficeBalancesQuery, IReadOnlyList<OfficeBalanceDto>>,
      IRequestHandler<ListOfficePayablesQuery, PagedResult<OfficePayableDto>>,
      IRequestHandler<ListPayableBookingHoldsQuery, IReadOnlyList<PayableHoldDto>>,
      IRequestHandler<ListOfficeSettlementsQuery, Result<PagedResult<OfficeSettlementDto>, Error>>,
      IRequestHandler<GetOfficeSettlementQuery, Result<OfficeSettlementDetailDto, Error>>,
      IRequestHandler<GetFinanceSummaryQuery, Result<FinanceSummaryDto, Error>>
{
    /// <summary>The longest span the finance summary answers for: a figure over more is a report, not a screen.</summary>
    private const int MaxFinanceDays = 366;

    public async Task<IReadOnlyList<OfficeBalanceDto>> Handle(GetOfficeBalancesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var balances = await ledger.BalancesAsync(request.DealerId, cancellationToken);
        return balances.Select(OfficeBalanceDto.ForAdmin).ToList();
    }

    public async Task<PagedResult<OfficePayableDto>> Handle(ListOfficePayablesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (page, pageSize) = Paging.Of(request.Page, request.PageSize);
        var listed = await ledger.ListPayablesAsync(
            new PayableListFilter(
                request.DealerId,
                PayableScope.Of(request.Scope),
                request.From is { } from ? calendar.StartOfDay(from) : null,
                request.To is { } to ? calendar.StartOfDay(to.AddDays(1)) : null,
                page,
                pageSize),
            cancellationToken);
        return new PagedResult<OfficePayableDto>(
            listed.Items.Select(OfficePayableDto.ForAdmin).ToList(), listed.Page, listed.PageSize, listed.TotalCount);
    }

    public async Task<IReadOnlyList<PayableHoldDto>> Handle(ListPayableBookingHoldsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var held = await ledger.ListBookingHoldsAsync(request.DealerId, cancellationToken);
        return held.Select(PayableHoldDto.From).ToList();
    }

    public async Task<Result<PagedResult<OfficeSettlementDto>, Error>> Handle(
        ListOfficeSettlementsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await ledger.OfficeNameAsync(request.DealerId, cancellationToken) is null)
            return PayableErrors.OfficeNotFound;

        var (page, pageSize) = Paging.Of(request.Page, request.PageSize);
        var listed = await ledger.ListSettlementsAsync(request.DealerId, page, pageSize, cancellationToken);
        return new PagedResult<OfficeSettlementDto>(
            listed.Items.Select(OfficeSettlementDto.ForAdmin).ToList(), listed.Page, listed.PageSize, listed.TotalCount);
    }

    public async Task<Result<OfficeSettlementDetailDto, Error>> Handle(GetOfficeSettlementQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var detail = await ledger.SettlementAsync(request.SettlementId, cancellationToken);
        return detail is null ? PayableErrors.SettlementNotFound : OfficeSettlementDetailDto.ForAdmin(detail);
    }

    public async Task<Result<FinanceSummaryDto, Error>> Handle(GetFinanceSummaryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var today = calendar.Today(clock.UtcNow);
        var from = request.From ?? new DateOnly(today.Year, today.Month, 1);
        var to = request.To ?? today;
        if (to < from || to.DayNumber - from.DayNumber >= MaxFinanceDays)
            return Error.Validation("payables.finance_span_invalid", $"Ask for a span of 1 to {MaxFinanceDays} days, its end on or after its start.");

        var summary = await ledger.FinanceSummaryAsync(from, to, calendar.StartOfDay(from), calendar.StartOfDay(to.AddDays(1)), cancellationToken);
        return new FinanceSummaryDto(
            from,
            to,
            summary.Totals.Select(FinanceTotalsDto.From).ToList(),
            summary.HeldBookings,
            summary.HeldPayables,
            summary.BlockedPayables);
    }
}

/// <summary>
/// The office's own payouts (payments Phase 8): its balance, its payables and its settlements, for the owner and for
/// an employee granted the financial reports (spec 4.2 / 4.5) — the same gate as the reports themselves.
/// </summary>
public sealed class OfficePayoutQueryHandlers(IOfficeLedgerReader ledger, DealerMembershipResolver membership)
    : IRequestHandler<GetMyPayoutsQuery, Result<OfficePayoutsDto, Error>>,
      IRequestHandler<ListMyPayablesQuery, Result<PagedResult<OfficePayableDto>, Error>>,
      IRequestHandler<ListMySettlementsQuery, Result<PagedResult<OfficeSettlementDto>, Error>>,
      IRequestHandler<GetMySettlementQuery, Result<OfficeSettlementDetailDto, Error>>
{
    public async Task<Result<OfficePayoutsDto, Error>> Handle(GetMyPayoutsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var office = await OfficeAsync(request.UserId, cancellationToken);
        if (office.IsFailure)
            return office.Error;

        var balances = await ledger.BalancesAsync(office.Value.Id, cancellationToken);
        return new OfficePayoutsDto(balances.Select(OfficeBalanceDto.ForOffice).ToList());
    }

    public async Task<Result<PagedResult<OfficePayableDto>, Error>> Handle(ListMyPayablesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var office = await OfficeAsync(request.UserId, cancellationToken);
        if (office.IsFailure)
            return office.Error;

        var (page, pageSize) = Paging.Of(request.Page, request.PageSize);
        var listed = await ledger.ListPayablesAsync(
            new PayableListFilter(office.Value.Id, PayableScope.Of(request.Scope), null, null, page, pageSize),
            cancellationToken);
        return new PagedResult<OfficePayableDto>(
            listed.Items.Select(OfficePayableDto.ForOffice).ToList(), listed.Page, listed.PageSize, listed.TotalCount);
    }

    public async Task<Result<PagedResult<OfficeSettlementDto>, Error>> Handle(ListMySettlementsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var office = await OfficeAsync(request.UserId, cancellationToken);
        if (office.IsFailure)
            return office.Error;

        var (page, pageSize) = Paging.Of(request.Page, request.PageSize);
        var listed = await ledger.ListSettlementsAsync(office.Value.Id, page, pageSize, cancellationToken);
        return new PagedResult<OfficeSettlementDto>(
            listed.Items.Select(OfficeSettlementDto.ForOffice).ToList(), listed.Page, listed.PageSize, listed.TotalCount);
    }

    public async Task<Result<OfficeSettlementDetailDto, Error>> Handle(GetMySettlementQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var office = await OfficeAsync(request.UserId, cancellationToken);
        if (office.IsFailure)
            return office.Error;

        // Another office's settlement is answered as one that does not exist.
        var detail = await ledger.SettlementAsync(request.SettlementId, cancellationToken);
        return detail is null || detail.Settlement.DealerId != office.Value.Id
            ? PayableErrors.SettlementNotFound
            : OfficeSettlementDetailDto.ForOffice(detail);
    }

    private async Task<Result<Dealer, Error>> OfficeAsync(Id userId, CancellationToken cancellationToken)
    {
        var member = await membership.ResolveAsync(userId, cancellationToken);
        if (member.IsFailure)
            return member.Error;
        // Spec 4.2 / 4.5: the office's money is the owner's, and an employee's only with the reports grant.
        return member.Value.CanViewReports ? member.Value.Dealer : DealerErrors.ReportsNotGranted;
    }
}

/// <summary>A list scope, read leniently: anything unknown is the open payables, the list most often wanted.</summary>
internal static class PayableScope
{
    public static string Of(string? scope) =>
        string.Equals(scope, PayableListScopes.Settled, StringComparison.OrdinalIgnoreCase) ? PayableListScopes.Settled
        : string.Equals(scope, PayableListScopes.All, StringComparison.OrdinalIgnoreCase) ? PayableListScopes.All
        : PayableListScopes.Open;
}

/// <summary>A page and its size, kept to what a screen can use.</summary>
internal static class Paging
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Of(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
}
