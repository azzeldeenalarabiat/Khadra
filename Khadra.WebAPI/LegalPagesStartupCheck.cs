using Khadra.Application.Common.Ports;

namespace Khadra.WebAPI;

/// <summary>
/// Says, on every start, whether the legal pages are being linked (Wave 2 G1).
/// </summary>
/// <remarks>
/// <c>/app-config</c> links each published legal text to its page on the customer website, built from
/// <c>App:CustomerAppBaseUrl</c>. While that is empty no link is offered anywhere, and the console's sign-in,
/// registration and invitation pages show none. That gap would otherwise be found by somebody looking for the
/// terms. Never fatal: the texts are still published and still served.
/// </remarks>
internal static partial class LegalPagesStartupCheck
{
    public static void Report(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var site = services.GetRequiredService<ICustomerSiteSettings>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Khadra.Legal");

        if (site.BaseUrl is { } baseUrl)
            LogLinked(logger, baseUrl.AbsoluteUri);
        else
            LogNotLinked(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Legal pages are linked on {BaseUrl}.")]
    private static partial void LogLinked(ILogger logger, string baseUrl);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "LEGAL PAGE LINKS ARE NOT PUBLISHED. App:CustomerAppBaseUrl is empty or not an http(s) address, so /app-config names no page for the Terms or the Privacy notice.")]
    private static partial void LogNotLinked(ILogger logger);
}
