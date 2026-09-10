using System.Net;
using System.Text;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Documents;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// The production document store: a PRIVATE Supabase bucket, reached only by this server.
/// </summary>
/// <remarks>
/// The local provider writes to the container's filesystem, which on an ephemeral platform means
/// every licence scan and every customer's identity document is deleted on the next deploy. This is
/// the store that survives, and these tests pin the two things that make it safe rather than merely
/// persistent: no URL of any kind leaves the server, and a bad credential never disguises itself as
/// a missing document.
/// </remarks>
public sealed class SupabaseDocumentStorageTests
{
    private const string Bucket = "khadra-documents";

    /// <summary>Answers whatever the test says, and keeps the request so the test can inspect it.</summary>
    private sealed class Recording(HttpStatusCode status, string body = "", byte[]? bytes = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = bytes is null
                    ? new StringContent(body)
                    : new ByteArrayContent(bytes),
            };
        }
    }

    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://project.supabase.co/storage/v1/") };
    }

    private static SupabaseDocumentStorage Storage(HttpMessageHandler handler) =>
        new(new OneClient(handler), Options.Create(new DocumentStorageOptions
        {
            Provider = DocumentStorageOptions.SupabaseProvider,
            Supabase = new SupabaseStorageOptions
            {
                Url = "https://project.supabase.co",
                Bucket = Bucket,
                ServiceKey = "not-a-real-key",
            },
        }));

    [Fact]
    public async Task Saving_posts_the_bytes_to_the_bucket_and_returns_a_generated_key()
    {
        var handler = new Recording(HttpStatusCode.OK, "{}");

        var stored = await Storage(handler).SaveAsync(
            "dealers/01a08980-01ee-73e8-a882-c7e5728c2d4a",
            "licence.jpg",
            "image/jpeg",
            new MemoryStream(Encoding.UTF8.GetBytes("bytes")));

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Contains($"object/{Bucket}/dealers/", handler.Request.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.EndsWith(".jpg", stored.StorageKey, StringComparison.Ordinal);
        // The key is generated, never the uploaded name: a caller cannot steer where bytes land.
        Assert.DoesNotContain("licence", stored.StorageKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A fresh guid per key means a collision is not a retry, it is a bug — so the store is told to
    /// refuse rather than overwrite, matching the local provider's FileMode.CreateNew.
    /// </summary>
    [Fact]
    public async Task A_new_document_is_never_allowed_to_overwrite_one()
    {
        var handler = new Recording(HttpStatusCode.OK, "{}");

        await Storage(handler).SaveAsync(
            "dealers/01a08980-01ee-73e8-a882-c7e5728c2d4a", "x.pdf", "application/pdf", new MemoryStream([1]));

        Assert.Equal("false", handler.Request!.Headers.GetValues("x-upsert").Single());
    }

    /// <summary>A missing document is not an error — the controller turns null into a 404.</summary>
    [Fact]
    public async Task A_document_that_is_not_there_reads_as_nothing()
    {
        var handler = new Recording(HttpStatusCode.NotFound, "{\"error\":\"not_found\"}");

        var content = await Storage(handler).OpenAsync("dealers/01a08980-01ee-73e8-a882-c7e5728c2d4a/abc123.jpg");

        Assert.Null(content);
    }

    /// <summary>
    /// The one that matters most. Letting 401/403 fall through to null would turn a revoked or
    /// mistyped service key into a plausible 404 on every tile an administrator opens: the platform
    /// would look as though it had never stored anything, and nobody would go and look at the
    /// configuration. It is loud, and it names the setting.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_credential_is_never_reported_as_a_missing_document(HttpStatusCode status)
    {
        var handler = new Recording(status, "{\"message\":\"Invalid JWT\"}");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Storage(handler).OpenAsync("dealers/01a08980-01ee-73e8-a882-c7e5728c2d4a/abc123.jpg"));

        Assert.Contains("ServiceKey", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_document_that_exists_is_streamed_back()
    {
        var handler = new Recording(HttpStatusCode.OK, bytes: Encoding.UTF8.GetBytes("the scan"));

        await using var content = await Storage(handler)
            .OpenAsync("dealers/01a08980-01ee-73e8-a882-c7e5728c2d4a/abc123.jpg");

        Assert.NotNull(content);
        using var reader = new StreamReader(content!);
        Assert.Equal("the scan", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Deleting_names_the_object_in_the_body_the_store_expects()
    {
        var handler = new Recording(HttpStatusCode.OK, "[]");
        const string key = "dealers/01a08980-01ee-73e8-a882-c7e5728c2d4a/abc123.jpg";

        await Storage(handler).DeleteAsync(key);

        Assert.Equal(HttpMethod.Delete, handler.Request!.Method);
        Assert.Contains("prefixes", handler.RequestBody!, StringComparison.Ordinal);
        Assert.Contains(key, handler.RequestBody!, StringComparison.Ordinal);
    }

    /// <summary>Already gone is the state deletion wanted.</summary>
    [Fact]
    public async Task Deleting_something_that_is_already_gone_is_not_an_error()
    {
        var handler = new Recording(HttpStatusCode.NotFound, "{}");

        await Storage(handler).DeleteAsync("dealers/01a08980-01ee-73e8-a882-c7e5728c2d4a/abc123.jpg");
    }

    /// <summary>
    /// A key becomes a URL PATH here, which a filesystem key never does. So the same whitelist the
    /// local provider uses is applied BEFORE any request is built — a leading slash would re-root the
    /// call, a '?' would start a query, and '..' might be collapsed by an intermediary before the
    /// store ever saw it.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/dealers/x/abc.jpg")]
    [InlineData("dealers/x/abc.jpg?download=1")]
    [InlineData("dealers/x/abc.jpg#fragment")]
    [InlineData("dealers/x/../../other-bucket/abc.jpg")]
    [InlineData("dealers/x/abc.exe")]
    [InlineData("dealers/x/abc.jpg/../../../secret.pdf")]
    public async Task A_key_this_platform_could_not_have_generated_never_reaches_the_network(string key)
    {
        var handler = new Recording(HttpStatusCode.OK, "{}");
        var storage = Storage(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenAsync(key));

        // Refused before the request was built, not after the store declined it.
        Assert.Null(handler.Request);
    }
}

/// <summary>
/// One whitelist for keys, shared by every provider.
/// </summary>
/// <remarks>
/// These rules used to live inside the local provider. A second provider would have grown its own,
/// and the two would have differed exactly where it matters: on a disk a bad key escapes a root,
/// against an object store the same key is a URL path.
/// </remarks>
public sealed class DocumentKeyTests
{
    [Theory]
    [InlineData("dealers/01a08980-01ee-73e8-a882-c7e5728c2d4a/abc123.jpg")]
    [InlineData("dealer-branding/01a08980-01ee-73e8-a882-c7e5728c2d4a/logo-abc123.png")]
    [InlineData("customers/01a08980-01ee-73e8-a882-c7e5728c2d4a/abc123.pdf")]
    public void A_key_this_platform_generates_is_accepted(string key) =>
        Assert.Equal(key, DocumentKeys.Validate(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-slashes.jpg")]
    [InlineData("UPPER/01a08980/abc.jpg")]
    [InlineData("dealers/01a08980/abc.jpg;.png")]
    public void Anything_else_is_refused(string? key) =>
        Assert.Throws<InvalidOperationException>(() => DocumentKeys.Validate(key));

    /// <summary>An extension nobody vouched for is never stored, whatever the upload was called.</summary>
    [Theory]
    [InlineData("scan.jpg", "image/jpeg", ".jpg")]
    [InlineData("scan.PDF", "application/pdf", ".pdf")]
    [InlineData("scan.exe", "image/png", ".png")]
    [InlineData("", "application/pdf", ".pdf")]
    [InlineData("scan.svg", "text/html", ".jpg")]
    public void The_extension_comes_from_a_fixed_list(string fileName, string contentType, string expected) =>
        Assert.Equal(expected, DocumentKeys.Extension(fileName, contentType));
}
