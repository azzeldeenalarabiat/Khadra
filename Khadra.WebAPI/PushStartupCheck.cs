using Khadra.Application.Common.Ports;

namespace Khadra.WebAPI;

/// <summary>
/// Says, on every start, whether customers' phones can be woken.
/// </summary>
/// <remarks>
/// Never fatal, like the mail check: without a push provider the platform still works, notifications
/// still appear in the app's own list, and reminder emails still go out. But booking updates and the
/// pickup and return reminders stop reaching a phone in a pocket, and that has to be said where an
/// operator will read it rather than discovered from a customer who missed their car.
/// </remarks>
internal static partial class PushStartupCheck
{
    public static void Report(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var sender = services.GetRequiredService<IPushSender>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Khadra.Push");

        if (sender.IsConfigured)
            LogReady(logger);
        else
            LogNotReady(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Push notifications ready (FCM).")]
    private static partial void LogReady(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "PUSH NOTIFICATIONS ARE NOT SENT. Push:Provider is None; set it to Fcm with a service account to wake customers' phones.")]
    private static partial void LogNotReady(ILogger logger);
}
