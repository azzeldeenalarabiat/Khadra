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
/// Phase 6). Minted when the customer asks, never listed ahead: the link lasts minutes.
/// </summary>
public sealed record GetMyFinancialDocumentPdfLinkQuery(Id CustomerId, Id DocumentId, string? Language)
    : IQuery<Result<SignedDocumentLink, Error>>;

/// <summary>A short-lived link to the PDF of any document, in <c>en</c> or <c>ar</c> — a voided one's included.</summary>
public sealed record GetAdminFinancialDocumentPdfLinkQuery(Id DocumentId, string? Language)
    : IQuery<Result<SignedDocumentLink, Error>>;

public sealed class GetMyFinancialDocumentPdfLinkQueryValidator : AbstractValidator<GetMyFinancialDocumentPdfLinkQuery>
{
    public GetMyFinancialDocumentPdfLinkQueryValidator() =>
        RuleFor(query => query.Language).Must(PdfLanguages.IsKnown).WithMessage(PdfLanguages.Refusal);
}

public sealed class GetAdminFinancialDocumentPdfLinkQueryValidator : AbstractValidator<GetAdminFinancialDocumentPdfLinkQuery>
{
    public GetAdminFinancialDocumentPdfLinkQueryValidator() =>
        RuleFor(query => query.Language).Must(PdfLanguages.IsKnown).WithMessage(PdfLanguages.Refusal);
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

/// <summary>
/// Mints the links (payments Phase 6). Whose document it is is decided FIRST, by the id written on the row at
/// issue, so a stranger asking about someone else's document is told what they would be told about one that
/// does not exist — never that it is there, and never whether its PDF is ready.
/// </summary>
public sealed class FinancialDocumentPdfLinkHandlers(
    IFinancialDocumentRepository documents,
    IFinancialDocumentRenditionRepository renditions,
    IDocumentLinkSigner signer,
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

        if (await documents.IsVoidedAsync(document.Id, cancellationToken))
            return FinancialDocumentErrors.PdfOfVoidedDocument;

        return await LinkAsync(document.Id, PdfLanguages.Parse(request.Language)!, cancellationToken);
    }

    public async Task<Result<SignedDocumentLink, Error>> Handle(GetAdminFinancialDocumentPdfLinkQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await documents.GetByIdAsync(request.DocumentId, cancellationToken) is null)
            return FinancialDocumentErrors.NotFound;

        return await LinkAsync(request.DocumentId, PdfLanguages.Parse(request.Language)!, cancellationToken);
    }

    private async Task<Result<SignedDocumentLink, Error>> LinkAsync(Id documentId, Language language, CancellationToken cancellationToken)
    {
        var rendition = await renditions.CurrentAsync(documentId, language, RenditionFormat.Pdf, cancellationToken);
        if (rendition is null)
            return FinancialDocumentErrors.PdfNotReady;

        return signer.Sign(rendition.StorageKey, clock.UtcNow);
    }
}
