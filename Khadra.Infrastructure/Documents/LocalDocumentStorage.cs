using System.Text.RegularExpressions;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Documents;

/// <summary>
/// Files on disk, outside the web root.
///
/// This is the local development and single-server implementation of spec 7's "access-controlled
/// object storage". Everything the port promises is honoured here: keys are generated rather than
/// taken from the caller, nothing is written where static-file middleware can reach it, and the only
/// way out is through the API. Replacing it with S3 or MinIO is a new class implementing the same
/// two methods; nothing above IDocumentStorage changes.
/// </summary>
internal sealed partial class LocalDocumentStorage : IDocumentStorage
{
    private readonly string _root;

    public LocalDocumentStorage(IOptions<DocumentStorageOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var configured = options.Value.RootPath;
        _root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);

        Directory.CreateDirectory(_root);
    }

    public async Task<StoredDocument> SaveAsync(
        string scope,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // The key is ours, not the uploader's. Only the extension survives from the supplied name,
        // and only from a fixed list, so a caller cannot write "..\\..\\appsettings.json".
        var key = $"{SafeScope(scope)}/{Guid.CreateVersion7():N}{DocumentKeys.Extension(fileName, contentType)}";
        var path = ResolveWithinRoot(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancellationToken);

        return new StoredDocument(key, contentType, file.Length);
    }

    public async Task<StoredDocument> SaveAtAsync(
        string storageKey,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // ResolveWithinRoot validates the shape and refuses anything escaping the root, so a key that
        // came back from a signed ticket still cannot point outside storage.
        var path = ResolveWithinRoot(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // CreateNew, not Create. The comment here always said "a ticket is meant to be spent once,
        // and silently replacing an existing file would make a replayed ticket look successful" --
        // while the code used FileMode.Create, which overwrites, and did exactly what the comment
        // forbade. The gap was a real window: an evidence ticket outlives the dispute it was minted
        // for, so whoever raised it could swap the file after the other party and an administrator
        // had read it, leaving the same key on the same record with different bytes inside.
        if (File.Exists(path))
            throw new DocumentAlreadyExistsException(storageKey);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancellationToken);

        return new StoredDocument(storageKey, contentType, file.Length);
    }

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = ResolveWithinRoot(storageKey);
        if (!File.Exists(path))
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = ResolveWithinRoot(storageKey);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a key under the root and refuses anything that escapes it. Belt and braces: keys are
    /// generated, but a stored key eventually comes back from the database, and a path check is far
    /// cheaper than trusting that every future write path stayed disciplined.
    /// </summary>
    private string ResolveWithinRoot(string storageKey)
    {
        DocumentKeys.Validate(storageKey);

        var candidate = Path.GetFullPath(Path.Combine(_root, storageKey));
        var root = Path.GetFullPath(_root);
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("The document key resolves outside the storage root.");

        return candidate;
    }

    private static string SafeScope(string scope) => DocumentKeys.ValidateScope(scope);


}
