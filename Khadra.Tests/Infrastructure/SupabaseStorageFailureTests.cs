using System.Net;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Documents;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// The failures that would otherwise be invisible: a wrong bucket, a refused key, a store that never
/// answers, and a second write at a key somebody already spent.
/// </summary>
/// <remarks>
/// Each of these was a real hole in the first version of this provider. They share one shape: the
/// wrong answer would have looked entirely ordinary — a missing document, a cancelled request, a
/// successful upload — so nothing would have prompted anyone to look.
/// </remarks>
public sealed class SupabaseStorageFailureTests
{
    private const string Key = "dealers/01a08980-01ee-73e8-a882-c7e5728c2d4a/abc123.jpg";

    private sealed class Answering(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    /// <summary>Throws what HttpClient itself throws when its own timeout elapses.</summary>
    private sealed class NeverAnswering : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public long? SentLength { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
            {
                // Reading it is what settles the length for a streamed body.
                var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                SentLength = bytes.LongLength;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
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
            MaximumSizeBytes = 8 * 1024 * 1024,
            Supabase = new SupabaseStorageOptions
            {
                Url = "https://project.supabase.co",
                Bucket = "khadra-documents",
                ServiceKey = "not-a-real-key",
            },
        }));

    /// <summary>
    /// Supabase answers 404 for a missing BUCKET as well as a missing object, so returning null on
    /// any 404 would turn a typo in Documents:Supabase:Bucket into a plausible "not found" on every
    /// tile an administrator opens — the platform looking as though it had never stored anything.
    /// </summary>
    [Fact]
    public async Task A_missing_bucket_is_not_reported_as_a_missing_document()
    {
        var storage = Storage(new Answering(HttpStatusCode.NotFound, "{\"error\":\"Bucket not found\"}"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenAsync(Key));

        Assert.Contains("Bucket", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A missing OBJECT still reads as nothing, which the controller turns into a 404.</summary>
    [Fact]
    public async Task A_missing_object_still_reads_as_nothing() =>
        Assert.Null(await Storage(new Answering(HttpStatusCode.NotFound, "{\"error\":\"not_found\"}")).OpenAsync(Key));

    /// <summary>
    /// storage-api answers a malformed key with 400 and "Invalid JWT", not 401, so checking the
    /// status alone would let a mistyped key fall through to a generic message.
    /// </summary>
    [Fact]
    public async Task A_key_rejected_with_400_is_still_recognised_as_a_credential_problem()
    {
        var storage = Storage(new Answering(HttpStatusCode.BadRequest, "{\"message\":\"Invalid JWT\"}"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenAsync(Key));

        Assert.Contains("ServiceKey", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A storage timeout must not arrive as a cancellation. The API maps every
    /// OperationCanceledException to 499 "the client went away", logged at Warning — so an outage
    /// would read as administrators closing tabs, with no error line anywhere to alert on.
    /// </summary>
    [Fact]
    public async Task A_store_that_never_answers_is_a_server_problem_not_a_cancelled_request()
    {
        var storage = Storage(new NeverAnswering());

        var failure = await Assert.ThrowsAsync<TimeoutException>(() => storage.OpenAsync(Key));

        Assert.IsNotType<OperationCanceledException>(failure);
        Assert.Contains("TimeoutSeconds", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ticket is spent once. Without this the person who raised a dispute could replace the evidence
    /// after the other party and an administrator had read it — same key, same record, new bytes.
    /// </summary>
    [Fact]
    public async Task A_second_write_at_a_committed_key_is_refused()
    {
        var storage = Storage(new Answering(HttpStatusCode.Conflict, "{\"error\":\"Duplicate\"}"));

        var failure = await Assert.ThrowsAsync<DocumentAlreadyExistsException>(
            () => storage.SaveAtAsync(Key, "image/jpeg", new MemoryStream([1, 2, 3])));

        Assert.Equal(Key, failure.StorageKey);
    }

    [Fact]
    public async Task A_ticketed_upload_never_asks_the_store_to_overwrite()
    {
        var recorder = new Recorder();

        await Storage(recorder).SaveAtAsync(Key, "image/jpeg", new MemoryStream([1, 2, 3]));

        Assert.Equal("false", recorder.Request!.Headers.GetValues("x-upsert").Single());
    }

    /// <summary>
    /// A request body cannot be rewound, so it is spooled before it is sent. That is what stops the
    /// store's timeout being tied to the phone's upload speed, and it is the only way the caller gets
    /// a real byte count rather than zero.
    /// </summary>
    [Fact]
    public async Task A_stream_that_cannot_be_rewound_is_still_measured_and_sent()
    {
        var recorder = new Recorder();
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);

        var stored = await Storage(recorder).SaveAtAsync(Key, "image/jpeg", new ForwardOnly(payload));

        Assert.Equal(payload.Length, stored.SizeBytes);
        Assert.Equal(payload.LongLength, recorder.SentLength);
    }

    /// <summary>An upload larger than the policy allows is stopped while it is being spooled.</summary>
    [Fact]
    public async Task An_oversized_upload_never_reaches_the_store()
    {
        var recorder = new Recorder();
        var storage = Storage(recorder);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.SaveAtAsync(Key, "image/jpeg", new ForwardOnly(new byte[9 * 1024 * 1024])));

        Assert.Null(recorder.Request);
    }

    /// <summary>A network stream: readable once, never seekable.</summary>
    private sealed class ForwardOnly(byte[] payload) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = Math.Min(count, payload.Length - _position);
            Array.Copy(payload, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>
/// What the startup probe refuses to let the platform run with.
/// </summary>
/// <remarks>
/// A public bucket is the one failure the application could never notice by itself: it never asks for
/// a public URL, so every object being readable at a guessable address with no credential would look,
/// from inside, exactly like a working private bucket.
/// </remarks>
public sealed class DocumentStoreProbeTests
{
    private sealed class Answering(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://project.supabase.co/storage/v1/") };
    }

    private static SupabaseStoreProbe Probe(HttpStatusCode status, string body) =>
        new(new OneClient(new Answering(status, body)), Options.Create(new DocumentStorageOptions
        {
            Provider = DocumentStorageOptions.SupabaseProvider,
            Supabase = new SupabaseStorageOptions
            {
                Url = "https://project.supabase.co",
                Bucket = "khadra-documents",
                ServiceKey = "not-a-real-key",
            },
        }));

    [Fact]
    public async Task A_private_bucket_that_answers_is_usable()
    {
        var status = await Probe(HttpStatusCode.OK, "{\"name\":\"khadra-documents\",\"public\":false}").CheckAsync();

        Assert.True(status.IsUsable);
        Assert.True(status.IsReachable);
    }

    /// <summary>Fatal. Licence scans and passports must not be one guessed name away from the world.</summary>
    [Fact]
    public async Task A_public_bucket_stops_the_platform_starting()
    {
        var status = await Probe(HttpStatusCode.OK, "{\"name\":\"khadra-documents\",\"public\":true}").CheckAsync();

        Assert.False(status.IsUsable);
        Assert.Contains("PUBLIC", status.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bucket_that_does_not_exist_stops_the_platform_starting()
    {
        var status = await Probe(HttpStatusCode.NotFound, "{\"error\":\"Bucket not found\"}").CheckAsync();

        Assert.False(status.IsUsable);
        Assert.Contains("no bucket named", status.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_credential_stops_the_platform_starting()
    {
        var status = await Probe(HttpStatusCode.BadRequest, "{\"message\":\"Invalid JWT\"}").CheckAsync();

        Assert.False(status.IsUsable);
        Assert.Contains("ServiceKey", status.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unreachable is weather, not a wrong setting. A blip at boot must not stop an API that serves
    /// everything else, and the readiness probe already reports it.
    /// </summary>
    [Fact]
    public async Task A_store_that_is_briefly_down_is_a_warning_not_a_crash()
    {
        var status = await Probe(HttpStatusCode.BadGateway, "upstream unavailable").CheckAsync();

        Assert.True(status.IsUsable);
        Assert.False(status.IsReachable);
    }
}
