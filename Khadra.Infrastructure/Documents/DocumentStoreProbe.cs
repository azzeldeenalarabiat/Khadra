using System.Net;
using System.Text.Json;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Documents;

/// <summary>What the platform found when it asked the document store whether it was ready.</summary>
/// <param name="IsUsable">False stops startup. This is configuration, not weather.</param>
/// <param name="IsReachable">False is a warning: a store that is briefly down is not a bad setting.</param>
/// <param name="Description">One sentence an operator can act on.</param>
public sealed record DocumentStoreStatus(bool IsUsable, bool IsReachable, string Description);

/// <summary>
/// Asks the document store, once at startup, whether it will actually work — and whether it is
/// private.
/// </summary>
/// <remarks>
/// The same reasoning as the mail transport probe, with one addition that only applies here. A bucket
/// created public, or flipped public in the dashboard months later, makes every licence scan and
/// every passport readable at a guessable address with no credential at all — and nothing in this
/// codebase could otherwise notice, because the application never asks for a public URL. So the
/// answer to "is it private" is checked at every boot, and a public bucket is fatal.
/// </remarks>
public interface IDocumentStoreProbe
{
    Task<DocumentStoreStatus> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>Nothing to ask: the directory is created and permission-checked at construction.</summary>
internal sealed class LocalStoreProbe(IOptions<DocumentStorageOptions> options) : IDocumentStoreProbe
{
    public Task<DocumentStoreStatus> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DocumentStoreStatus(true, true,
            $"Documents:Provider is 'Local'. Files are written to {options.Value.RootPath} INSIDE this " +
            "container, so they are deleted on the next deploy. Correct for development; not storage."));
}

internal sealed class SupabaseStoreProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<DocumentStorageOptions> options) : IDocumentStoreProbe
{
    public async Task<DocumentStoreStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        var bucket = options.Value.Supabase.Bucket!;
        var client = httpClientFactory.CreateClient(SupabaseDocumentStorage.HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync($"bucket/{bucket}", cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // Unreachable is not the same as misconfigured. A network blip at boot must not stop an
            // API that serves everything else, and the readiness probe already reports it.
            return new DocumentStoreStatus(true, false,
                $"Could not reach Supabase Storage to check the '{bucket}' bucket — {exception.Message}");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
                (response.StatusCode == HttpStatusCode.BadRequest &&
                 body.Contains("Invalid JWT", StringComparison.OrdinalIgnoreCase)))
            {
                return new DocumentStoreStatus(false, true,
                    "Supabase Storage refused this server's credentials. Documents:Supabase:ServiceKey " +
                    "must be a secret key for this project — an 'sb_secret_…' key, or the legacy " +
                    $"'service_role' key. Detail: {body}");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new DocumentStoreStatus(false, true,
                    $"Supabase Storage has no bucket named '{bucket}' in this project. Create it, " +
                    "PRIVATE, before starting — the API will not create it for you, because a bucket " +
                    "created by accident is a bucket nobody chose the visibility of.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new DocumentStoreStatus(true, false,
                    $"Supabase Storage answered {(int)response.StatusCode} when asked about the " +
                    $"'{bucket}' bucket: {body}");
            }

            // THE CHECK THAT ONLY EXISTS HERE. A public bucket serves every object at
            // /storage/v1/object/public/{bucket}/{key} with no credential, which would put identity
            // documents on the open internet behind nothing but an unguessable name. The application
            // never asks for a public URL, so it would never find out on its own.
            if (IsPublic(body))
            {
                return new DocumentStoreStatus(false, true,
                    $"The Supabase bucket '{bucket}' is PUBLIC. Every document in it — licence scans, " +
                    "identity papers — is readable by anyone with the object name, with no credential. " +
                    "Set the bucket to private in the Supabase dashboard before starting.");
            }

            return new DocumentStoreStatus(true, true,
                $"Supabase Storage is reachable and the '{bucket}' bucket is private.");
        }
    }

    private static bool IsPublic(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("public", out var value) &&
                   value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            // A body we cannot read is not a claim that the bucket is private.
            return false;
        }
    }
}
