using Khadra.Application.Common.Ports;

namespace Khadra.WebAPI;

/// <summary>
/// Says, on every start, whether the platform can actually take a deposit.
/// </summary>
/// <remarks>
/// The twin of <see cref="MailStartupCheck"/>, and it exists for the same reason: a platform that
/// silently cannot take payments looks exactly like one that can, right up to the first customer who
/// tries. Today the answer is always no — no provider is configured — and the line at boot is what
/// stops that being a surprise. Never fatal: every other part of the platform works without it, and
/// a customer is told the truth on their own booking.
/// </remarks>
internal static partial class PaymentsStartupCheck
{
    public static async Task ReportAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var provider = services.GetRequiredService<IPaymentProvider>();
        var detail = await services.GetRequiredService<IPaymentProviderProbe>().DescribeAsync(cancellationToken);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Khadra.Payments");

        if (provider.IsConfigured)
            LogReady(logger, detail);
        else
            LogNotReady(logger, detail);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Payments ready. {Detail}")]
    private static partial void LogReady(ILogger logger, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PAYMENTS ARE NOT ACCEPTED. {Detail}")]
    private static partial void LogNotReady(ILogger logger, string detail);
}
