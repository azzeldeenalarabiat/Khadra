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
    /// EMPTY by default, and empty is a real shipping state rather than a missing value — the owner
    /// confirmed on 2026-09-11 that it stays empty until there is somewhere real to send people.
    /// While it is, the emails name the booking reference and tell the reader to open the app, which
    /// is true and useful.
    ///
    /// <b>NEVER point a customer at <c>ClientBaseUrl</c>.</b> That is the dealer and admin console,
    /// and a customer following it lands on a sign-in that refuses them — at the exact moment they
    /// are trying to pay. Two settings exist so that cannot happen by accident.
    ///
    /// What belongs here eventually is one customer-facing HTTPS host whose
    /// <c>/bookings/{id}</c> opens the app through an Android App Link or an iOS Universal Link, and
    /// falls back to the customer website when the app is not installed. Not a custom scheme: it
    /// cannot fall back and many mail clients will not linkify it. See pre-launch item 91.
    /// </remarks>
    public string CustomerAppBaseUrl { get; init; } = string.Empty;

    // Browser origins allowed to call the API directly (normally only the BFF talks to the API).
    public string[] AllowedOrigins { get; init; } = [];
}
