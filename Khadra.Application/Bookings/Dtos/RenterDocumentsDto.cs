using Khadra.Application.Common.Ports;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.Bookings.Dtos;

/// <summary>
/// One of the renter's documents, as the GALLERY handling their booking sees it.
/// </summary>
/// <param name="ContentType">What the download will actually be served as, so a tile cannot mislabel a PDF.</param>
/// <remarks>
/// Deliberately not <c>CustomerDocumentDto</c>, even though four of the fields match. That shape is
/// the owner's view of their own paperwork and carries <c>ReviewNote</c> -- the words somebody wrote
/// when rejecting a photograph, addressed to the customer and none of a gallery's business. A shared
/// DTO would have left one field's audience to be decided by whoever added the next one.
///
/// There is no URL and no storage key here, and deliberately nowhere to put one. The bytes come from
/// a sibling endpoint that re-checks the booking relationship on every request; a link on this shape
/// would be a standing grant that outlived the rule which issued it.
/// </remarks>
public sealed record RenterDocumentDto(
    Guid DocumentId,
    string Type,
    string Status,
    string ContentType,
    DateTimeOffset UploadedAt)
{
    public static RenterDocumentDto From(CustomerDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new RenterDocumentDto(
            document.Id.Value,
            document.Type.Name,
            document.Status.Name,
            // From the KEY, not from the stored content type, so this agrees with the byte-serving
            // endpoint by construction: both ask DocumentContentTypes the same question. The admin
            // review screen learned this the hard way, labelling every registration ".jpg".
            DocumentContentTypes.ForStorageKey(document.StorageKey),
            document.UploadedAt);
    }
}

/// <summary>
/// What the gallery may see about the renter's paperwork on one of its own live bookings.
/// </summary>
/// <param name="Documents">Everything this renter has on file, in the order spec 5.1 asks for it.</param>
/// <param name="IsComplete">The customer's own record's answer, never a count this list re-derived.</param>
/// <param name="Missing">
/// The required documents this renter has not filed, named so the panel can say WHICH is absent
/// rather than showing an empty box.
/// </param>
public sealed record RenterDocumentsDto(
    IReadOnlyList<RenterDocumentDto> Documents,
    bool IsComplete,
    IReadOnlyList<string> Missing);
