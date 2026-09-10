using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Documents;

/// <summary>
/// Documents in a PRIVATE Supabase Storage bucket, which is the only storage a container has that
/// survives the container.
/// </summary>
/// <remarks>
/// The local provider writes to the filesystem, and on a platform with an ephemeral disk that means
/// every licence scan and every customer's identity document is gone on the next deploy.
///
/// NO URL LEAVES THIS CLASS. The bucket is private, and this does not mint Supabase signed URLs
/// either, so the store has no public surface and no time-limited one. A document is reached exactly
/// as it always was: an administrator opens a link this platform signed, on this platform's domain,
/// <c>DocumentsController</c> checks the HMAC, and the bytes are streamed back with
/// <c>Cache-Control: no-store, private</c>. That is one authorisation path and one clock. Signed URLs
/// would be a second way in that our authorisation never sees and that we cannot revoke.
///
/// Every failure here is deliberately loud EXCEPT a genuinely missing object, because the whole point
/// of moving off the disk was to stop documents disappearing quietly. In particular a refused
/// credential and a wrong bucket name are NOT reported as a missing document, even though Supabase
/// answers 404 for a wrong bucket exactly as it does for a wrong key.
/// </remarks>
internal sealed class SupabaseDocumentStorage : IDocumentStorage
{
    public const string HttpClientName = "supabase-storage";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _bucket;
    private readonly long _maximumSizeBytes;
    private readonly int _timeoutSeconds;

    public SupabaseDocumentStorage(IHttpClientFactory httpClientFactory, IOptions<DocumentStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _httpClientFactory = httpClientFactory;
        // Validated at startup (AddDocumentStorage), so these are never null in a running process.
        _bucket = options.Value.Supabase.Bucket!;
        _maximumSizeBytes = options.Value.MaximumSizeBytes;
        _timeoutSeconds = options.Value.Supabase.TimeoutSeconds;
    }

    public Task<StoredDocument> SaveAsync(
        string scope,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // The key is ours, not the uploader's -- same rule and same shape as the local provider. Only
        // the extension survives from the supplied name, and only from a fixed list.
        var key = $"{DocumentKeys.ValidateScope(scope)}/{Guid.CreateVersion7():N}{DocumentKeys.Extension(fileName, contentType)}";
        return UploadAsync(key, contentType, content, cancellationToken);
    }

    public Task<StoredDocument> SaveAtAsync(
        string storageKey,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return UploadAsync(DocumentKeys.Validate(storageKey), contentType, content, cancellationToken);
    }

    /// <summary>
    /// Writes the bytes, and never over bytes that are already there.
    /// </summary>
    /// <remarks>
    /// <c>x-upsert: false</c> on BOTH saves. For a generated key a collision is not a retry, it is a
    /// bug worth hearing about. For a key committed by a presigned ticket it is the security answer:
    /// a ticket is meant to be spent once, and a second write would let the person who raised a
    /// dispute replace the evidence after it had been read.
    /// </remarks>
    private async Task<StoredDocument> UploadAsync(
        string key,
        string contentType,
        Stream content,
        CancellationToken cancellationToken)
    {
        // A request body arrives as a non-seekable network stream, and handing it straight to the
        // store would tie the store's timeout to the PHONE's upload speed: a customer on a slow
        // connection would blow a thirty-second budget sending an 8 MB dispute photo, and the failure
        // would arrive as a cancellation. Spooled to a temp file first, so the upload to Supabase is
        // a local read of known length under a timeout that means what it says. It also gives the
        // store a Content-Length, and gives the caller a real byte count instead of zero.
        await using var spooled = await SpoolAsync(content, cancellationToken);
        var payload = spooled ?? content;
        var size = payload.CanSeek ? payload.Length : 0;

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var body = new StreamContent(payload);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"object/{_bucket}/{key}") { Content = body };
        request.Headers.TryAddWithoutValidation("x-upsert", "false");

        using var response = await SendAsync(client, request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.IsSuccessStatusCode)
            return new StoredDocument(key, contentType, size);

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);

        // Already stored is its own answer, not a server error: a client retrying after a lost
        // response should be told the bytes are there, and go on to the confirmation step.
        if (response.StatusCode == HttpStatusCode.Conflict || IsAlreadyExists(detail))
            throw new DocumentAlreadyExistsException(key);

        ThrowIfCredentialProblem(response.StatusCode, detail);
        throw new InvalidOperationException(
            $"Supabase Storage refused the upload ({(int)response.StatusCode} {response.ReasonPhrase}): {detail}");
    }

    /// <summary>
    /// Copies a stream that cannot be rewound into a temp file, so its length is known before it is
    /// sent. Returns null when the stream already knows its own length.
    /// </summary>
    private async Task<Stream?> SpoolAsync(Stream content, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
            return null;

        // DeleteOnClose: the file is gone when the stream is disposed, including on an exception, so
        // a failed upload leaves no copy of somebody's passport in the container's temp directory.
        var spool = new FileStream(
            Path.Combine(Path.GetTempPath(), $"khadra-upload-{Guid.CreateVersion7():N}"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        try
        {
            // Bounded by the same limit the callers enforce. Without this a client could stream
            // without end and fill the container's disk before anything checked a size.
            await CopyBoundedAsync(content, spool, _maximumSizeBytes, cancellationToken);
            spool.Position = 0;
            return spool;
        }
        catch
        {
            await spool.DisposeAsync();
            throw;
        }
    }

    private static async Task CopyBoundedAsync(Stream from, Stream to, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await from.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit)
                throw new InvalidOperationException($"The upload is larger than the {limit}-byte limit.");

            await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    public async Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var key = DocumentKeys.Validate(storageKey);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"object/{_bucket}/{key}");
        var response = await SendAsync(client, request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            // The response owns the network stream, so it must outlive this method: the caller
            // disposes the stream, and that releases the response with it.
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }

        var status = response.StatusCode;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        response.Dispose();

        // A 404 is TWO different situations and only one of them is ordinary. Supabase answers 404
        // for a missing object AND for a missing bucket, so returning null on any 404 would turn a
        // typo in Documents:Supabase:Bucket into a plausible "not found" on every tile an
        // administrator opens -- the platform looking as though it had never stored anything, with
        // nobody prompted to go and read the configuration. Only a missing OBJECT reads as nothing.
        if (status == HttpStatusCode.NotFound)
        {
            if (IsMissingObject(detail))
                return null;

            throw new InvalidOperationException(
                "Supabase Storage answered 404, but not for a missing object -- most likely " +
                $"Documents:Supabase:Bucket names a bucket that does not exist in this project. Detail: {detail}");
        }

        ThrowIfCredentialProblem(status, detail);
        throw new InvalidOperationException(
            $"Supabase Storage could not return the document ({(int)status} {response.ReasonPhrase}): {detail}");
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var key = DocumentKeys.Validate(storageKey);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        // Deletion takes the object names in a body rather than the URL, and answers 200 whether or
        // not anything matched. Already gone is the state we wanted.
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"object/{_bucket}")
        {
            Content = JsonContent.Create(new { prefixes = new[] { key } }),
        };

        using var response = await SendAsync(client, request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            return;

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfCredentialProblem(response.StatusCode, detail);
        throw new InvalidOperationException(
            $"Supabase Storage refused the delete ({(int)response.StatusCode} {response.ReasonPhrase}): {detail}");
    }

    /// <summary>
    /// Sends the request, and makes sure a STORAGE timeout never masquerades as the caller hanging up.
    /// </summary>
    /// <remarks>
    /// HttpClient reports its own timeout as <see cref="TaskCanceledException"/>, and the API's
    /// exception handler maps every cancellation to 499 "the client went away", logged at Warning.
    /// So a storage outage would read, in the log, as administrators closing tabs -- with no error
    /// line anywhere and nothing to alert on. Rethrown as something that lands in the 500 arm.
    /// </remarks>
    private async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        HttpCompletionOption completion,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(request, completion, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Supabase Storage did not answer within {_timeoutSeconds} seconds " +
                "(Documents:Supabase:TimeoutSeconds).");
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"Supabase Storage could not be reached: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// A refused credential is never allowed to look like anything else.
    /// </summary>
    /// <remarks>
    /// storage-api answers a malformed key with 400 and the body "Invalid JWT", not 401, so checking
    /// the status alone would let a mistyped key fall through to a generic message. The body decides.
    /// </remarks>
    private static void ThrowIfCredentialProblem(HttpStatusCode status, string detail)
    {
        var refused =
            status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
            (status == HttpStatusCode.BadRequest &&
             detail.Contains("Invalid JWT", StringComparison.OrdinalIgnoreCase));

        if (!refused) return;

        throw new InvalidOperationException(
            $"Supabase Storage refused this server's credentials ({(int)status}). " +
            "Documents:Supabase:ServiceKey must be a secret key for this project -- an 'sb_secret_…' " +
            "key, or the legacy 'service_role' key -- and the bucket must exist in the same project. " +
            $"Detail: {detail}");
    }

    private static bool IsMissingObject(string detail) =>
        detail.Contains("not_found", StringComparison.OrdinalIgnoreCase) ||
        detail.Contains("NoSuchKey", StringComparison.OrdinalIgnoreCase) ||
        detail.Contains("Object not found", StringComparison.OrdinalIgnoreCase);

    private static bool IsAlreadyExists(string detail) =>
        detail.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
        detail.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) ||
        detail.Contains("ResourceAlreadyExists", StringComparison.OrdinalIgnoreCase);
}
