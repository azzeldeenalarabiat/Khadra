using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.FinancialDocuments.Repositories;
using MediatR;

namespace Khadra.Application.FinancialDocuments.Queries;

/// <summary>
/// A short-lived link to the PDF of one of the customer's documents, in <c>en</c> or <c>ar</c> (payments
/// Phase 6). Minted when the customer asks, never listed ahead: the link lasts minutes. For a voided document it
/// is the voided copy — stamped VOID, naming its correction — never the unstamped original (owner, 2026-09-29).
/// </summary>
public sealed record GetMyFinancialDocumentPdfLinkQuery(Id CustomerId, Id DocumentId, string? Language)
    : IQuery<Result<SignedDocumentLink, Error>>;

/// <summary>
/// A short-lived link to the PDF of any document, in <c>en</c> or <c>ar</c>: the document as issued by default — a
/// voided one's included, unstamped — or, with <paramref name="Kind"/> <c>Voided</c>, the voided copy its customer
/// is given.
/// </summary>
public sealed record GetAdminFinancialDocumentPdfLinkQuery(Id DocumentId, string? Language, string? Kind = null)
    : IQuery<Result<SignedDocumentLink, Error>>;

public sealed class GetMyFinancialDocumentPdfLinkQueryValidator : AbstractValidator<GetMyFinancialDocumentPdfLinkQuery>
{
    public GetMyFinancialDocumentPdfLinkQueryValidator() =>
        RuleFor(query => query.Language).Must(PdfLanguages.IsKnown).WithMessage(PdfLanguages.Refusal);
}

public sealed class GetAdminFinancialDocumentPdfLinkQueryValidator : AbstractValidator<GetAdminFinancialDocumentPdfLinkQuery>
{
    public GetAdminFinancialDocumentPdfLinkQueryValidator()
    {
        RuleFor(query => query.Language).Must(PdfLanguages.IsKnown).WithMessage(PdfLanguages.Refusal);
        RuleFor(query => query.Kind).Must(PdfKinds.IsKnown).WithMessage(PdfKinds.Refusal);
    }
}

/// <summary>The two languages a PDF is drawn in, spelled as the platform spells them everywhere.</summary>
internal static class PdfLanguages
{
    public const string Refusal = "Ask for the PDF in en or ar.";

    public static bool IsKnown(string? name) => Parse(name) is not null;

    public static Language? Parse(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : Enumeration.GetAll<Language>().FirstOrDefault(language => string.Equals(language.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Which rendition the administrator asks for: <c>AsIssued</c> when nothing is said, or <c>Voided</c>.</summary>
internal static class PdfKinds
{
    public const string Refusal = "Ask for the PDF as AsIssued or Voided.";

    public static bool IsKnown(string? name) => string.IsNullOrWhiteSpace(name) || Parse(name) is not null;

    public static RenditionKind? Parse(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? RenditionKind.AsIssued
            : Enumeration.GetAll<RenditionKind>().FirstOrDefault(kind => string.Equals(kind.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Mints the links (payments Phase 6). Whose document it is is decided FIRST, by the id written on the row at
/// issue, so a stranger asking about someone else's document is told what they would be told about one that
/// does not exist — never that it is there, never whether it was voided, and never whether its PDF is ready.
/// </summary>
public sealed class FinancialDocumentPdfLinkHandlers(
    IFinancialDocumentRepository documents,
    IFinancialDocumentRenditionRepository renditions,
    IDocumentLinkSigner signer,
    ICurrentActor actor,
    IClock clock)
    : IRequestHandler<GetMyFinancialDocumentPdfLinkQuery, Result<SignedDocumentLink, Error>>,
      IRequestHandler<GetAdminFinancialDocumentPdfLinkQuery, Result<SignedDocumentLink, Error>>
{
    public async Task<Result<SignedDocumentLink, Error>> Handle(GetMyFinancialDocumentPdfLinkQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = await documents.GetByIdAsync(request.DocumentId, cancellationToken);
        if (document is null || document.CustomerId != request.CustomerId)
            return FinancialDocumentErrors.NotFound;

        // A voided document's customer is given its voided copy; the unstamped original stays the administrators'.
        var kind = await documents.IsVoidedAsync(document.Id, cancellationToken) ? RenditionKind.Voided : RenditionKind.AsIssued;
        return await LinkAsync(document.Id, PdfLanguages.Parse(request.Language)!, kind, request.CustomerId, cancellationToken);
    }

    public async Task<Result<SignedDocumentLink, Error>> Handle(GetAdminFinancialDocumentPdfLinkQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await documents.GetByIdAsync(request.DocumentId, cancellationToken) is null)
            return FinancialDocumentErrors.NotFound;

        var kind = PdfKinds.Parse(request.Kind)!;
        if (kind == RenditionKind.Voided && !await documents.IsVoidedAsync(request.DocumentId, cancellationToken))
            return FinancialDocumentErrors.NotVoided;

        // Bound to the administrator who asked (pre-launch item 14). The route is administrators-only, so somebody is
        // signed in; a request with nobody behind it is a programming error, not a document to hand out.
        var viewer = actor.UserId ?? throw new InvalidOperationException("A document link is minted for the signed-in person who will open it.");
        return await LinkAsync(request.DocumentId, PdfLanguages.Parse(request.Language)!, kind, viewer, cancellationToken);
    }

    private async Task<Result<SignedDocumentLink, Error>> LinkAsync(
        Id documentId, Language language, RenditionKind kind, Id viewer, CancellationToken cancellationToken)
    {
        var rendition = await renditions.CurrentAsync(documentId, language, RenditionFormat.Pdf, kind, cancellationToken);
        if (rendition is null)
            return FinancialDocumentErrors.PdfNotReady;

        return signer.Sign(rendition.StorageKey, viewer, clock.UtcNow);
    }
}
