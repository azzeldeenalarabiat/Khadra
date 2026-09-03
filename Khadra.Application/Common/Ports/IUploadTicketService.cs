namespace Khadra.Application.Common.Ports;

/// <summary>Where to PUT the bytes, and the key the file will be stored under.</summary>
public sealed record UploadTicket(string UploadUrl, string StorageKey, DateTimeOffset ExpiresAt);

/// <summary>What a redeemed ticket permits: this key, this content type, once.</summary>
public sealed record RedeemedUpload(string StorageKey, string ContentType);

/// <summary>
/// The presigned-upload flow spec 4.3 asks for, in three steps: ask for a URL, PUT the bytes to it,
/// then confirm the attachment.
///
/// The local implementation signs a short-lived ticket against our own API rather than a bucket, but
/// the SHAPE is deliberately the same as an S3 presigned PUT. That matters because the Flutter app
/// and the console both code against this flow: moving to real object storage later replaces one
/// adapter and changes no client.
///
/// The ticket binds the key AND the content type, so a caller cannot obtain a URL for a small JPEG
/// and then use it to upload something else somewhere else.
/// </summary>
public interface IUploadTicketService
{
    UploadTicket Issue(string storageKey, string contentType, DateTimeOffset now);

    bool TryRedeem(string token, DateTimeOffset now, out RedeemedUpload upload);
}
