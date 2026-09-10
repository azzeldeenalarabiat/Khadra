using Khadra.Infrastructure.Documents;

namespace Khadra.WebAPI;

/// <summary>
/// Says, on every start, where documents go and whether that place is private.
/// </summary>
/// <remarks>
/// The twin of <see cref="MailStartupCheck"/>, with one difference that decides its shape: this one
/// CAN be fatal.
///
/// Mail is never fatal because a relay being briefly unreachable is weather, and the platform reports
/// a failed send to the caller anyway. A document store is not like that. Two of its failures are
/// configuration rather than weather, and both are silent:
///
///   * the bucket does not exist, or the credential is refused — every upload would fail at the
///     moment a dealer finishes an application form, which is the worst place to find out;
///   * the bucket is PUBLIC — every licence scan and every passport is readable by anyone with the
///     object name and no credential at all, and nothing in the application would ever notice,
///     because it never asks for a public URL.
///
/// So an unusable store stops startup, and an unreachable one is a warning. A wrong setting is worth
/// a crash-loop; a network blip is not.
/// </remarks>
internal static partial class DocumentStoreStartupCheck
{
    public static async Task ReportAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var status = await services.GetRequiredService<IDocumentStoreProbe>().CheckAsync(cancellationToken);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Khadra.Documents");

        if (!status.IsUsable)
            throw new InvalidOperationException($"Document storage is not usable. {status.Description}");

        if (status.IsReachable)
            LogReady(logger, status.Description);
        else
            LogUnreachable(logger, status.Description);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Document storage. {Detail}")]
    private static partial void LogReady(ILogger logger, string detail);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "DOCUMENT STORAGE DID NOT ANSWER. {Detail} Uploads will fail until it does; nothing " +
                  "already stored is lost.")]
    private static partial void LogUnreachable(ILogger logger, string detail);
}
