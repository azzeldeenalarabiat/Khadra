using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common.Ports;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Configuration;

// Spec 7: identity papers and dealer commercial documents are sensitive personal data. They are
// written to a private root that is never served as static files, and reached only through a
// short-lived signed link.
public sealed class DocumentStorageOptions
{
    public const string SectionName = "Documents";

    /// <summary>Files on this machine's disk. The default, and what development and the tests use.</summary>
    public const string LocalProvider = "Local";

    /// <summary>A private Supabase Storage bucket. What production uses, because a container's disk is not storage.</summary>
    public const string SupabaseProvider = "Supabase";

    /// <summary>
    /// Where documents actually live.
    /// </summary>
    /// <remarks>
    /// Local by default, deliberately: it is the right answer for development and for the tests, and
    /// a platform that has not chosen a store should use the one that needs nothing configured rather
    /// than half-configure a remote one. Production sets Supabase, and an unrecognised value is
    /// refused at startup rather than falling back to a store that quietly loses everything on the
    /// next deploy.
    /// </remarks>
    public string Provider { get; init; } = LocalProvider;

    public SupabaseStorageOptions Supabase { get; init; } = new();

    /// <summary>
    /// Where files are written. Relative paths resolve against the content root. It must never point
    /// inside wwwroot: static-file middleware would then serve every customer's passport by URL.
    /// </summary>
    [Required]
    public string RootPath { get; init; } = "App_Data/documents";

    [Range(1024, 50 * 1024 * 1024)]
    public long MaximumSizeBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>
    /// Photographs of a licence or ID, or a scanned PDF. Nothing executable.
    /// </summary>
    /// <remarks>
    /// Empty by default, and configuration is the only source. A seeded default here does NOT get
    /// replaced by the configured list -- the binder APPENDS to a collection that already has
    /// items -- so the four types in appsettings became eight, each one twice. Nothing broke,
    /// because every use was a Contains check, and it stayed invisible until the app-config
    /// endpoint published the list to a client. An empty default plus the existing "at least one"
    /// validation means a missing key fails at startup instead.
    /// </remarks>
    public IReadOnlyList<string> AllowedContentTypes { get; init; } = [];

    [Range(1, 60)]
    public int LinkLifetimeMinutes { get; init; } = 5;
}

/// <summary>
/// Reaching a PRIVATE Supabase Storage bucket, server-side only.
/// </summary>
/// <remarks>
/// Nothing here is a URL a browser ever sees. The bucket is private, no signed URL is minted, and the
/// service key never leaves the server: a document is reached the same way it always was, through a
/// short-lived link this platform signed, on this platform's own domain.
/// </remarks>
public sealed class SupabaseStorageOptions
{
    /// <summary>The project's base address, e.g. <c>https://abcdefgh.supabase.co</c>.</summary>
    public string? Url { get; init; }

    /// <summary>The bucket name. It must be created as PRIVATE; a public bucket defeats the whole design.</summary>
    public string? Bucket { get; init; }

    /// <summary>
    /// The service_role key.
    /// </summary>
    /// <remarks>
    /// A real secret, and a wide one: it bypasses row-level security across the whole project. It
    /// belongs in the environment and nowhere else — never in a tracked appsettings file, never in
    /// render.yaml, never in a log line. Nothing in this codebase prints it.
    /// </remarks>
    public string? ServiceKey { get; init; }

    /// <summary>A person is waiting on an upload or a document tile, so this fails rather than hangs.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 30;
}

internal sealed class DocumentPolicySettings(IOptions<DocumentStorageOptions> options) : IDocumentPolicySettings
{
    public long MaximumSizeBytes => options.Value.MaximumSizeBytes;

    public IReadOnlyCollection<string> AllowedContentTypes => options.Value.AllowedContentTypes;

    public TimeSpan LinkLifetime => TimeSpan.FromMinutes(options.Value.LinkLifetimeMinutes);
}
