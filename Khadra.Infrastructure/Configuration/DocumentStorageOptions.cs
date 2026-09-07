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

internal sealed class DocumentPolicySettings(IOptions<DocumentStorageOptions> options) : IDocumentPolicySettings
{
    public long MaximumSizeBytes => options.Value.MaximumSizeBytes;

    public IReadOnlyCollection<string> AllowedContentTypes => options.Value.AllowedContentTypes;

    public TimeSpan LinkLifetime => TimeSpan.FromMinutes(options.Value.LinkLifetimeMinutes);
}
