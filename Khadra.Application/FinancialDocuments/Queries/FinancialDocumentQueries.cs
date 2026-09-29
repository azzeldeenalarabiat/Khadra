using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.Email;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using MediatR;

namespace Khadra.Application.FinancialDocuments.Queries;

/// <summary>The customer's Invoices &amp; Receipts area: every document of theirs, newest first.</summary>
public sealed record ListMyFinancialDocumentsQuery(Id CustomerId, string? Type, int? Page, int? PageSize)
    : IQuery<Result<PagedResult<FinancialDocumentListItem>, Error>>;

/// <summary>One of the customer's documents. Anyone else's is not found — never a hint that it exists.</summary>
public sealed record GetMyFinancialDocumentQuery(Id CustomerId, Id DocumentId) : IQuery<Result<FinancialDocumentDto, Error>>;

/// <summary>A booking's documents for Booking Details. The customer's; for the rental office, none.</summary>
public sealed record GetBookingFinancialDocumentsQuery(Id UserId, Id BookingId) : IQuery<Result<BookingFinancialDocumentsDto, Error>>;

/// <summary>Every document on the platform, filtered, newest first.</summary>
/// <param name="From">The first Amman calendar day of issue, inclusive.</param>
/// <param name="To">The last Amman calendar day of issue, inclusive.</param>
public sealed record ListAdminFinancialDocumentsQuery(
    string? Type,
    string? Status,
    string? Number,
    string? Reference,
    DateOnly? From,
    DateOnly? To,
    int? Page,
    int? PageSize) : IQuery<Result<PagedResult<AdminFinancialDocumentListItem>, Error>>;

public sealed record GetAdminFinancialDocumentQuery(Id DocumentId) : IQuery<Result<AdminFinancialDocumentDto, Error>>;

public sealed record GetAdminBookingFinancialDocumentsQuery(Id BookingId) : IQuery<Result<AdminBookingFinancialDocumentsDto, Error>>;

/// <summary>Families owed a document and not issued, oldest failure first.</summary>
public sealed record ListFinancialDocumentHoldsQuery(int? Page, int? PageSize) : IQuery<Result<PagedResult<FinancialDocumentHoldDto>, Error>>;

public sealed record GetFinancialDocumentVocabularyQuery : IQuery<Result<FinancialDocumentVocabularyDto, Error>>;

public sealed class ListMyFinancialDocumentsQueryValidator : AbstractValidator<ListMyFinancialDocumentsQuery>
{
    public ListMyFinancialDocumentsQueryValidator() =>
        RuleFor(query => query.Type)
            .Must(type => type is null || FinancialDocumentFilters.Parse<FinancialDocumentType>(type) is not null)
            .WithMessage("Unknown document type.");
}

public sealed class ListAdminFinancialDocumentsQueryValidator : AbstractValidator<ListAdminFinancialDocumentsQuery>
{
    public ListAdminFinancialDocumentsQueryValidator()
    {
        RuleFor(query => query.Type)
            .Must(type => type is null || FinancialDocumentFilters.Parse<FinancialDocumentType>(type) is not null)
            .WithMessage("Unknown document type.");
        RuleFor(query => query.Status)
            .Must(status => status is null || FinancialDocumentFilters.Parse<FinancialDocumentStatus>(status) is not null)
            .WithMessage("Unknown document status.");
        RuleFor(query => query.Number).MaximumLength(FinancialDocumentNumbers.MaxLength);
        RuleFor(query => query.Reference).MaximumLength(FinancialDocument.BookingReferenceMaxLength);
        RuleFor(query => query.To)
            .Must((query, to) => query.From is null || to is null || to >= query.From)
            .WithMessage("The range ends before it starts.");
    }
}

/// <summary>Turning a request's words into the domain's own values.</summary>
internal static class FinancialDocumentFilters
{
    public static T? Parse<T>(string? name) where T : Enumeration =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : Enumeration.GetAll<T>().FirstOrDefault(item => string.Equals(item.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The customer's readings of their documents (payments Phase 5). A document is the customer's own when its
/// booking is: the id on the row, written from the booking at issue, is what access is decided by.
/// </summary>
public sealed class CustomerFinancialDocumentQueryHandlers(
    IFinancialDocumentReader reader,
    IBookingRepository bookings,
    BookingPartyResolver parties)
    : IRequestHandler<ListMyFinancialDocumentsQuery, Result<PagedResult<FinancialDocumentListItem>, Error>>,
      IRequestHandler<GetMyFinancialDocumentQuery, Result<FinancialDocumentDto, Error>>,
      IRequestHandler<GetBookingFinancialDocumentsQuery, Result<BookingFinancialDocumentsDto, Error>>
{
    public async Task<Result<PagedResult<FinancialDocumentListItem>, Error>> Handle(
        ListMyFinancialDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await reader.ListForCustomerAsync(
            request.CustomerId,
            FinancialDocumentFilters.Parse<FinancialDocumentType>(request.Type),
            PageRequest.From(request.Page, request.PageSize),
            cancellationToken);
        return new PagedResult<FinancialDocumentListItem>(
            [.. page.Items.Select(FinancialDocumentListItem.From)],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    public async Task<Result<FinancialDocumentDto, Error>> Handle(GetMyFinancialDocumentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var record = await reader.GetAsync(request.DocumentId, cancellationToken);
        if (record is null || record.CustomerId != request.CustomerId)
            return FinancialDocumentErrors.NotFound;

        return (await FinancialDocumentPageReader.ReadAsync(reader, record, cancellationToken)).Page;
    }

    public async Task<Result<BookingFinancialDocumentsDto, Error>> Handle(
        GetBookingFinancialDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var booking = await bookings.GetByIdAsync(request.BookingId, cancellationToken);
        if (booking is null)
            return BookingErrors.NotFound;

        // A stranger is told the booking does not exist, exactly as GET /bookings/{id} tells them.
        var party = await parties.ResolveAsync(booking, request.UserId, cancellationToken);
        if (party.IsFailure)
            return party.Error;

        // The rental office sees no customer document in Phase 5: receipts carry processing fees and a
        // dispute refund carries the customer's share (decisions 8 and 3). An empty answer, not a refusal
        // that would say documents exist.
        if (party.Value != BookingParty.Customer)
            return new BookingFinancialDocumentsDto(booking.Id.Value, [], []);

        var documents = await reader.ListForBookingAsync(booking.Id, cancellationToken);
        var pending = await reader.PendingForBookingAsync(booking.Id, cancellationToken);
        return new BookingFinancialDocumentsDto(
            booking.Id.Value,
            [.. documents.Select(FinancialDocumentListItem.From)],
            [.. pending.Select(PendingFinancialDocumentDto.From)]);
    }
}

/// <summary>The administrator's readings of documents and holds (payments Phase 5).</summary>
public sealed class AdminFinancialDocumentQueryHandlers(
    IFinancialDocumentReader reader,
    IBookingRepository bookings,
    IReportingCalendar calendar,
    IFinancialDocumentEmailSettings emailSettings)
    : IRequestHandler<ListAdminFinancialDocumentsQuery, Result<PagedResult<AdminFinancialDocumentListItem>, Error>>,
      IRequestHandler<GetAdminFinancialDocumentQuery, Result<AdminFinancialDocumentDto, Error>>,
      IRequestHandler<GetAdminBookingFinancialDocumentsQuery, Result<AdminBookingFinancialDocumentsDto, Error>>,
      IRequestHandler<ListFinancialDocumentHoldsQuery, Result<PagedResult<FinancialDocumentHoldDto>, Error>>,
      IRequestHandler<GetFinancialDocumentVocabularyQuery, Result<FinancialDocumentVocabularyDto, Error>>
{
    public async Task<Result<PagedResult<AdminFinancialDocumentListItem>, Error>> Handle(
        ListAdminFinancialDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var filter = new AdminFinancialDocumentFilter(
            FinancialDocumentFilters.Parse<FinancialDocumentType>(request.Type),
            FinancialDocumentFilters.Parse<FinancialDocumentStatus>(request.Status),
            string.IsNullOrWhiteSpace(request.Number) ? null : request.Number.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim().ToUpperInvariant(),
            request.From is { } first ? calendar.StartOfDay(first) : null,
            request.To is { } last ? calendar.StartOfDay(last.AddDays(1)) : null);
        var page = await reader.ListAsync(filter, PageRequest.From(request.Page, request.PageSize), cancellationToken);
        return new PagedResult<AdminFinancialDocumentListItem>(
            [.. page.Items.Select(AdminFinancialDocumentListItem.From)],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    public async Task<Result<AdminFinancialDocumentDto, Error>> Handle(GetAdminFinancialDocumentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var record = await reader.GetAsync(request.DocumentId, cancellationToken);
        if (record is null)
            return FinancialDocumentErrors.NotFound;

        var (page, voided, renditions) = await FinancialDocumentPageReader.ReadAsync(reader, record, cancellationToken);
        var emails = await reader.DeliveriesOfAsync(record.Id, cancellationToken);
        return new AdminFinancialDocumentDto(
            page,
            record.CustomerId.Value,
            record.DealerId.Value,
            record.Provider,
            Domain.Payments.PaymentProviders.IsSandbox(record.Provider),
            record.ContentSha256,
            record.CoversThrough,
            record.CheckpointFingerprint,
            voided is null
                ? null
                : new FinancialDocumentVoidDto(voided.VoidedAt, voided.VoidedByAdminId.Value, voided.VoidedByName, voided.Reason, page.Links.ReplacedBy),
            [.. renditions.Select(FinancialDocumentRenditionDto.From)],
            [.. emails.Select(FinancialDocumentEmailDto.From)],
            // Only a receipt is emailed, never a voided one, one email at a time, and none from a server whose
            // delivery is switched off (owner, 2026-09-29).
            CanEmailAgain: record.Type.IsEmailedToCustomer
                && voided is null
                && emails.All(email => email.State != FinancialDocumentDeliveryState.Queued)
                && emailSettings.DeliveryDisabledReason is null,
            EmailDeliveryDisabled: emailSettings.DeliveryDisabledReason is not null);
    }

    public async Task<Result<AdminBookingFinancialDocumentsDto, Error>> Handle(
        GetAdminBookingFinancialDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await bookings.GetByIdAsync(request.BookingId, cancellationToken) is null)
            return BookingErrors.NotFound;

        // One after another: the reader's queries share this request's context.
        var documents = await reader.ListForBookingAsync(request.BookingId, cancellationToken);
        var pending = await reader.PendingForBookingAsync(request.BookingId, cancellationToken);
        var holds = await reader.OpenHoldsForBookingAsync(request.BookingId, cancellationToken);
        return new AdminBookingFinancialDocumentsDto(
            request.BookingId.Value,
            [.. documents.Select(AdminFinancialDocumentListItem.From)],
            [.. pending.Select(PendingFinancialDocumentDto.From)],
            [.. holds.Select(FinancialDocumentHoldDto.From)]);
    }

    public async Task<Result<PagedResult<FinancialDocumentHoldDto>, Error>> Handle(
        ListFinancialDocumentHoldsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await reader.ListOpenHoldsAsync(PageRequest.From(request.Page, request.PageSize), cancellationToken);
        return new PagedResult<FinancialDocumentHoldDto>(
            [.. page.Items.Select(FinancialDocumentHoldDto.From)],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    public Task<Result<FinancialDocumentVocabularyDto, Error>> Handle(
        GetFinancialDocumentVocabularyQuery request,
        CancellationToken cancellationToken) =>
        Task.FromResult<Result<FinancialDocumentVocabularyDto, Error>>(new FinancialDocumentVocabularyDto(
            [.. Enumeration.GetAll<FinancialDocumentType>().Select(value => value.Name)],
            [.. Enumeration.GetAll<FinancialDocumentStatus>().Select(value => value.Name)],
            [.. Enumeration.GetAll<FinancialDocumentCause>().Select(value => value.Name)],
            [.. Enumeration.GetAll<IssuanceHoldReason>().Select(value => value.Name)]));
}

/// <summary>Reads what a document's page needs besides the document: its family, its receipts, its void.</summary>
internal static class FinancialDocumentPageReader
{
    public static async Task<(FinancialDocumentDto Page, FinancialDocumentVoidRecord? Void, IReadOnlyList<FinancialDocumentRenditionRecord> Renditions)> ReadAsync(
        IFinancialDocumentReader reader,
        FinancialDocumentRecord record,
        CancellationToken cancellationToken)
    {
        // One after another: the reader's queries share the request's context.
        var family = await reader.FamilyAsync(record.Type, record.SubjectId, cancellationToken);
        var paymentReceipt = record.RelatedDocumentId is { } related
            ? await reader.GetAsync(related, cancellationToken)
            : null;
        IReadOnlyList<FinancialDocumentRecord> refundReceipts = [];
        if (record.Type == FinancialDocumentType.PaymentReceipt && record.PaymentId is { } paymentId)
        {
            // The CURRENT receipt of each refund made from this payment: one per refund.
            refundReceipts =
            [
                .. (await reader.ListForPaymentAsync(paymentId, cancellationToken))
                    .Where(candidate => candidate.Type == FinancialDocumentType.RefundReceipt)
                    .GroupBy(candidate => candidate.SubjectId)
                    .Select(versions => versions.MaxBy(candidate => candidate.Version)!)
                    .OrderBy(candidate => candidate.OccurredAt)
                    .ThenBy(candidate => candidate.Number, StringComparer.Ordinal),
            ];
        }

        var voided = await reader.VoidOfAsync(record.Id, cancellationToken);
        var renditions = await reader.RenditionsOfAsync(record.Id, cancellationToken);
        return (FinancialDocumentPages.Build(record, family, paymentReceipt, refundReceipts, voided, renditions), voided, renditions);
    }
}
