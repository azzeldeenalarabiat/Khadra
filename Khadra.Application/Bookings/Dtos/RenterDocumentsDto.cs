using Khadra.Application.Common.Ports;
using Khadra.Domain.Bookings;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.Bookings.Dtos;

/// <summary>
/// What THIS dealership recorded about looking at one document.
/// </summary>
/// <remarks>
/// The wording this shape is allowed to support is "reviewed by the dealer", never "verified". It
/// says a named member of the gallery's staff opened the file the renter uploaded and satisfied
/// themselves; it makes no claim that the document is authentic, current, or checked against any
/// register. Khadra does not do that, and pre-launch item 27 is where whether anyone ever will gets
/// decided.
/// </remarks>
public sealed record DealerDocumentReviewDto(
    DateTimeOffset ReviewedAt,
    Guid ReviewedByUserId,
    string ReviewedByName)
{
    public static DealerDocumentReviewDto From(RenterDocumentReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        return new DealerDocumentReviewDto(
            review.ReviewedAt, review.ReviewedByUserId.Value, review.ReviewedByName);
    }
}

/// <summary>
/// One of the renter's documents, as the GALLERY handling their booking sees it.
/// </summary>
/// <param name="ContentType">What the download will actually be served as, so a tile cannot mislabel a PDF.</param>
/// <param name="UploadedAt">
/// When the renter filed THIS version. A document row is a slot whose file is replaced in place when
/// they re-photograph it, so this is what tells a gallery the picture changed.
/// </param>
/// <param name="DealerReview">
/// Null when this dealership has not reviewed THIS upload — including when it reviewed an earlier
/// one and the renter has since replaced the file. The server decides that; the screen never
/// computes it from two dates.
/// </param>
/// <remarks>
/// <para>
/// Deliberately not <c>CustomerDocumentDto</c>, even though some fields match. That shape is the
/// owner's view of their own paperwork and carries <c>ReviewNote</c> — the words somebody wrote when
/// rejecting a photograph, addressed to the customer and none of a gallery's business.
/// </para>
/// <para>
/// <b>There is deliberately no platform review status here.</b> <c>CustomerDocument.Status</c> is
/// Khadra's own field, unused because nothing on the platform verifies a document, and it can take
/// the value <c>Verified</c>. Beside a dealer's own review it would read as "Khadra has not verified
/// this YET", which promises a check nobody has decided to build; and the day item 27 lands, a green
/// platform tick would appear on a gallery's screen with no decision to show one. Taking a field back
/// after galleries have seen it is a contract change, so it never goes out.
/// </para>
/// <para>
/// No URL and no storage key, and nowhere to put one. The bytes come from a sibling endpoint that
/// re-checks the booking relationship on every request.
/// </para>
/// </remarks>
public sealed record RenterDocumentDto(
    Guid DocumentId,
    string Type,
    string ContentType,
    DateTimeOffset UploadedAt,
    DealerDocumentReviewDto? DealerReview)
{
    public static RenterDocumentDto From(CustomerDocument document, RenterDocumentReview? review)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new RenterDocumentDto(
            document.Id.Value,
            document.Type.Name,
            // From the KEY, not from the stored content type, so this agrees with the byte-serving
            // endpoint by construction: both ask DocumentContentTypes the same question. The admin
            // review screen learned this the hard way, labelling every registration ".jpg".
            DocumentContentTypes.ForStorageKey(document.StorageKey),
            document.UploadedAt,
            review is null ? null : DealerDocumentReviewDto.From(review));
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
