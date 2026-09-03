using Khadra.Application.Common.Ports;

namespace Khadra.Tests.Support;

// Records what a handler asked to store, without touching a disk.
internal sealed class FakeDocumentStorage : IDocumentStorage
{
    public List<(string Key, string Scope, long Size)> Saved { get; } = [];
    public List<string> Deleted { get; } = [];

    public Task<StoredDocument> SaveAsync(
        string scope,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var key = $"{scope}/{Guid.CreateVersion7():N}.jpg";
        var size = content.CanSeek ? content.Length : 1024;
        Saved.Add((key, scope, size));
        return Task.FromResult(new StoredDocument(key, contentType, size));
    }

    public Task<StoredDocument> SaveAtAsync(
        string storageKey,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var size = content.CanSeek ? content.Length : 1024;
        Saved.Add((storageKey, storageKey, size));
        return Task.FromResult(new StoredDocument(storageKey, contentType, size));
    }

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(new MemoryStream([1, 2, 3]));

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        Deleted.Add(storageKey);
        return Task.CompletedTask;
    }
}

internal sealed class FakeDocumentPolicy : IDocumentPolicySettings
{
    public static readonly FakeDocumentPolicy Default = new();

    public long MaximumSizeBytes => 8 * 1024 * 1024;

    public IReadOnlyCollection<string> AllowedContentTypes { get; } =
        ["image/jpeg", "image/png", "image/webp", "application/pdf"];

    public TimeSpan LinkLifetime => TimeSpan.FromMinutes(5);
}
