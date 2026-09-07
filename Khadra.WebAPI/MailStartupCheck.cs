using Khadra.Application.Common.Ports;

namespace Khadra.WebAPI;

/// <summary>
/// Says, on every start, whether mail will actually be delivered.
///
/// Registration, the administrator invitation and every password reset depend on it, and until this
/// existed the only way to discover a misconfiguration was for someone to register and then wait at
/// an inbox nothing was coming to. Never fatal: a mail outage must not stop the API serving
/// everything else, and the platform reports a failed send to the caller anyway.
/// </summary>
internal static partial class MailStartupCheck
{
    public static async Task ReportAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var status = await services.GetRequiredService<IEmailTransportProbe>().CheckAsync(cancellationToken);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Khadra.Email");

        if (status.IsReady)
            LogReady(logger, status.Description);
        else
            LogNotReady(logger, status.Description);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email ready. {Detail}")]
    private static partial void LogReady(ILogger logger, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "EMAIL WILL NOT BE DELIVERED. {Detail}")]
    private static partial void LogNotReady(ILogger logger, string detail);
}
