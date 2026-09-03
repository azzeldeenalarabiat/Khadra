using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.IdentityAccess.Dtos;

/// <summary>
/// A customer's document as the owner of it sees it.
///
/// There is no URL and no storage key on this shape, deliberately. Spec 7 wants these files reachable
/// only through a short-lived signed link, so the client asks for one explicitly when it needs to
/// display the file; a key on the listing would be a standing invitation to build a permanent URL.
/// </summary>
public sealed record CustomerDocumentDto(
    Guid DocumentId,
    string Type,
    string Status,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    string? ReviewNote)
{
    public static CustomerDocumentDto From(CustomerDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new CustomerDocumentDto(
            document.Id.Value,
            document.Type.Name,
            document.Status.Name,
            document.SizeBytes,
            document.UploadedAt,
            document.ReviewNote);
    }
}

/// <summary>
/// The document checklist, so a client can tell a customer what is still missing before they book.
/// </summary>
public sealed record CustomerDocumentsDto(
    IReadOnlyList<CustomerDocumentDto> Documents,
    bool IsComplete,
    IReadOnlyList<string> Missing);
