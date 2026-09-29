using Khadra.Application.Common.Ports;

namespace Khadra.Tests.Support;

/// <summary>
/// Document storage that keeps the bytes in memory (payments Phase 6), so a test can open what a handler
/// stored and hash it — and can make the store refuse, keep something other than it was given, or run a
/// step of the test at the moment a write or a read arrives.
/// </summary>
internal sealed class MemoryDocumentStorage : IDocumentStorage
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, byte[]> Files => _files;

    public List<string> Deleted { get; } = [];

    /// <summary>Every write throws, as a store that is down does.</summary>
    public bool Refuse { get; set; }

    /// <summary>Every write keeps this many bytes fewer than it was given.</summary>
    public int Truncate { get; set; }

    /// <summary>Runs as a write arrives, before it lands: where a test stages a second writer.</summary>
    public Func<string, Task>? BeforeWrite { get; set; }

    public Task<StoredDocument> SaveAsync(
        string scope,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default) =>
        SaveAtAsync($"{scope}/{Guid.CreateVersion7():N}.pdf", contentType, content, cancellationToken);

    public async Task<StoredDocument> SaveAtAsync(
        string storageKey,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (BeforeWrite is { } before)
            await before(storageKey);
        if (Refuse)
            throw new IOException("The document store is not answering.");
        if (_files.ContainsKey(storageKey))
            throw new DocumentAlreadyExistsException(storageKey);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray()[..Math.Max(0, (int)buffer.Length - Truncate)];
        _files[storageKey] = bytes;
        return new StoredDocument(storageKey, contentType, bytes.LongLength);
    }

    /// <summary>Puts other bytes under a key already written, as a store that altered or lost a file would.</summary>
    public void Replace(string storageKey, byte[] bytes)
    {
        if (!_files.ContainsKey(storageKey))
            throw new InvalidOperationException($"Nothing is stored at {storageKey}.");
        _files[storageKey] = bytes;
    }

    /// <summary>Runs as a read arrives, before it is answered: where a test stages another process mid-step (payments Phase 7).</summary>
    public Func<string, Task>? BeforeOpen { get; set; }

    public async Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (BeforeOpen is { } before)
            await before(storageKey);
        return _files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes, writable: false) : null;
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        Deleted.Add(storageKey);
        _files.Remove(storageKey);
        return Task.CompletedTask;
    }
}
