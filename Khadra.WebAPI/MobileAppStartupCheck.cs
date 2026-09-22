using Khadra.Application.Common.Ports;

namespace Khadra.WebAPI;

/// <summary>
/// Says on every start which customer-app builds this API will serve.
/// </summary>
/// <remarks>
/// Raising <c>MobileApp:MinimumSupportedVersion</c> turns away every older phone at once. That should
/// be read in the boot log by whoever deployed it, not discovered from a customer's update screen.
/// </remarks>
internal static partial class MobileAppStartupCheck
{
    public static void Report(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var policy = services.GetRequiredService<IMobileAppPolicySettings>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Khadra.MobileApp");

        if (policy.MinimumSupportedVersion is not { } minimum)
        {
            LogNoMinimum(logger);
            return;
        }

        LogMinimum(logger, minimum.ToString(), policy.UpdateUrl?.AbsoluteUri ?? "(none published)");
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Customer app: no minimum version is set, so every build is served.")]
    private static partial void LogNoMinimum(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Customer app: builds older than {Minimum} are REFUSED with 426 app.update_required, and so is any build that sends the app User-Agent without X-Khadra-App-Version. Update link: {UpdateUrl}.")]
    private static partial void LogMinimum(ILogger logger, string minimum, string updateUrl);
}
