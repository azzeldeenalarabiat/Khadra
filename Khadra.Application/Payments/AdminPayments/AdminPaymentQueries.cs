using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.Queries;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.Payments.Financials;
using Khadra.Application.Payments.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using MediatR;

namespace Khadra.Application.Payments.AdminPayments;

/// <summary>
/// The administrator's payments list (payments Phase 4b): every checkout attempt, newest first.
/// </summary>
/// <param name="From">The first Amman calendar day of the range, inclusive.</param>
/// <param name="To">The last Amman calendar day of the range, inclusive.</param>
public sealed record ListAdminPaymentsQuery(
    string? Status,
    string? Purpose,
    Guid? DealerId,
    Guid? CustomerId,
    string? Reference,
    DateOnly? From,
    DateOnly? To,
    int? Page,
    int? PageSize) : IQuery<Result<PagedResult<AdminPaymentListItem>, Error>>;

/// <summary>
/// The refunds queue (payments Phase 4b; the human surface pre-launch item 157 asks for). No status is
/// the live queue: refused first, then recorded, then sent.
/// </summary>
public sealed record ListAdminRefundsQuery(
    string? Status,
    string? Reason,
    string? Reference,
    DateOnly? From,
    DateOnly? To,
    int? Page,
    int? PageSize) : IQuery<Result<PagedResult<AdminRefundListItem>, Error>>;

/// <summary>One payment's page: the attempt and its refunds, its booking, and what the provider said.</summary>
public sealed record GetAdminPaymentQuery(Id PaymentId) : IQuery<Result<AdminPaymentDto, Error>>;

/// <summary>The words the payments screens filter on, as the domain names them.</summary>
public sealed record GetPaymentVocabularyQuery : IQuery<Result<PaymentVocabularyDto, Error>>;

/// <summary>
/// Every value the payments list and the refunds queue can be filtered on (payments Phase 4b), read from
/// the domain's own enumerations. The console keeps no list of its own: a filter set that went stale
/// would hide a status the server had started to use.
/// </summary>
public sealed record PaymentVocabularyDto(
    IReadOnlyList<string> PaymentStatuses,
    IReadOnlyList<string> PaymentPurposes,
    IReadOnlyList<string> RefundStatuses,
    IReadOnlyList<string> RefundReasons);

/// <summary>
/// One payment as the administrator sees it (payments Phase 4b).
/// </summary>
/// <remarks>
/// <see cref="Payment"/> is the SAME description a booking's financial state gives it
/// (<c>BookingFinancialsCalculator.Describe</c>, the administrator's projection), so the payment reads
/// identically on its booking and on its own page. The live checkout URL is never part of it.
/// </remarks>
/// <param name="Booking">The booking it belongs to, or null when that no longer resolves.</param>
/// <param name="Documents">
/// The documents issued about this payment (payments Phase 5): every version of its receipt and of its
/// refunds' receipts, newest first. Added last; nothing else in this response changed.
/// </param>
public sealed record AdminPaymentDto(
    FinancialPaymentDto Payment,
    PaymentBookingLink? Booking,
    IReadOnlyList<ProviderEventItem> ProviderEvents,
    IReadOnlyList<AdminFinancialDocumentListItem>? Documents = null);

public sealed class ListAdminPaymentsQueryValidator : AbstractValidator<ListAdminPaymentsQuery>
{
    public ListAdminPaymentsQueryValidator()
    {
        RuleFor(query => query.Status)
            .Must(status => status is null || AdminPaymentFilters.Parse<PaymentStatus>(status) is not null)
            .WithMessage("Unknown payment status.");
        RuleFor(query => query.Purpose)
            .Must(purpose => purpose is null || AdminPaymentFilters.Parse<PaymentPurpose>(purpose) is not null)
            .WithMessage("Unknown payment purpose.");
        RuleFor(query => query.To)
            .Must((query, to) => query.From is null || to is null || to >= query.From)
            .WithMessage("The range ends before it starts.");
    }
}

public sealed class ListAdminRefundsQueryValidator : AbstractValidator<ListAdminRefundsQuery>
{
    public ListAdminRefundsQueryValidator()
    {
        RuleFor(query => query.Status)
            .Must(status => status is null || AdminPaymentFilters.Parse<RefundStatus>(status) is not null)
            .WithMessage("Unknown refund status.");
        RuleFor(query => query.Reason)
            .Must(reason => reason is null || AdminPaymentFilters.Parse<RefundReason>(reason) is not null)
            .WithMessage("Unknown refund reason.");
        RuleFor(query => query.To)
            .Must((query, to) => query.From is null || to is null || to >= query.From)
            .WithMessage("The range ends before it starts.");
    }
}

/// <summary>Turning a request's words into the domain's own values.</summary>
internal static class AdminPaymentFilters
{
    /// <summary>The smart-enum value a name stands for, ignoring case; null for none.</summary>
    public static T? Parse<T>(string? name) where T : Enumeration =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : Enumeration.GetAll<T>().FirstOrDefault(item => string.Equals(item.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed class AdminPaymentQueryHandlers(
    IPaymentAdminReader reader,
    IPaymentRepository payments,
    IFinancialDocumentReader documents,
    IReportingCalendar calendar)
    : IRequestHandler<ListAdminPaymentsQuery, Result<PagedResult<AdminPaymentListItem>, Error>>,
      IRequestHandler<ListAdminRefundsQuery, Result<PagedResult<AdminRefundListItem>, Error>>,
      IRequestHandler<GetAdminPaymentQuery, Result<AdminPaymentDto, Error>>,
      IRequestHandler<GetPaymentVocabularyQuery, Result<PaymentVocabularyDto, Error>>
{
    public Task<Result<PaymentVocabularyDto, Error>> Handle(GetPaymentVocabularyQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<Result<PaymentVocabularyDto, Error>>(new PaymentVocabularyDto(
            [.. Enumeration.GetAll<PaymentStatus>().Select(value => value.Name)],
            [.. Enumeration.GetAll<PaymentPurpose>().Select(value => value.Name)],
            [.. Enumeration.GetAll<RefundStatus>().Select(value => value.Name)],
            [.. Enumeration.GetAll<RefundReason>().Select(value => value.Name)]));

    public async Task<Result<PagedResult<AdminPaymentListItem>, Error>> Handle(
        ListAdminPaymentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (from, before) = Range(request.From, request.To);
        var filter = new AdminPaymentFilter(
            AdminPaymentFilters.Parse<PaymentStatus>(request.Status),
            AdminPaymentFilters.Parse<PaymentPurpose>(request.Purpose),
            request.DealerId is { } dealerId ? Id.From(dealerId) : null,
            request.CustomerId is { } customerId ? Id.From(customerId) : null,
            request.Reference,
            from,
            before);
        return await reader.ListPaymentsAsync(filter, PageRequest.From(request.Page, request.PageSize), cancellationToken);
    }

    public async Task<Result<PagedResult<AdminRefundListItem>, Error>> Handle(
        ListAdminRefundsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (from, before) = Range(request.From, request.To);
        var filter = new AdminRefundFilter(
            AdminPaymentFilters.Parse<RefundStatus>(request.Status),
            AdminPaymentFilters.Parse<RefundReason>(request.Reason),
            request.Reference,
            from,
            before);
        return await reader.ListRefundsAsync(filter, PageRequest.From(request.Page, request.PageSize), cancellationToken);
    }

    public async Task<Result<AdminPaymentDto, Error>> Handle(GetAdminPaymentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payment = await payments.GetByIdAsync(request.PaymentId, cancellationToken);
        if (payment is null)
            return PaymentErrors.NotFound;

        // One after another: the repository and the readers share this request's DbContext.
        var booking = await reader.BookingLinkAsync(payment.BookingId, cancellationToken);
        var events = await reader.ProviderEventsAsync(payment.Id, payment.Provider, payment.ProviderReference, cancellationToken);
        var issued = await documents.ListForPaymentAsync(payment.Id, cancellationToken);

        return new AdminPaymentDto(
            FinancialPaymentDto.For(BookingFinancialsCalculator.Describe(payment), Reader.Of(BookingParty.Admin)),
            booking,
            events,
            [.. issued.Select(AdminFinancialDocumentListItem.From)]);
    }

    /// <summary>Amman calendar days, inclusive at both ends, as a half-open range of instants.</summary>
    private (DateTimeOffset? From, DateTimeOffset? Before) Range(DateOnly? from, DateOnly? to) =>
        (from is { } first ? calendar.StartOfDay(first) : null,
         to is { } last ? calendar.StartOfDay(last.AddDays(1)) : null);
}
