using System.ComponentModel.DataAnnotations;

namespace Khadra.Infrastructure.Configuration;

public sealed class AppOptions
{
    public const string SectionName = "App";

    // Base URL of the client that renders /verify-email and /reset-password (dashboard through the BFF).
    [Required, Url]
    public string ClientBaseUrl { get; init; } = "https://localhost:7243";

    /// <summary>
    /// Where a customer's own booking lives, for the links in the emails they are sent.
    /// </summary>
    /// <remarks>
    /// EMPTY by default, and empty is a real shipping state rather than a missing value: there is no
    /// customer web app and no deep link registered for the Flutter one yet. An email that linked to
    /// <c>ClientBaseUrl</c> would land a customer on the dealer and admin console's sign-in, which
    /// refuses them — so while this is empty the emails name the booking reference and tell the
    /// reader to open the app, which is true and useful. Set it to an https origin or an app link
    /// and the button appears. See pre-launch item 91.
    /// </remarks>
    public string CustomerAppBaseUrl { get; init; } = string.Empty;

    // Browser origins allowed to call the API directly (normally only the BFF talks to the API).
    public string[] AllowedOrigins { get; init; } = [];
}
