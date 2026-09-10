namespace Khadra.Infrastructure.Configuration;

/// <summary>
/// Which reverse-geocoding provider to ask, and the manners its usage policy requires.
/// </summary>
/// <remarks>
/// The defaults are the safe ones: no provider, so the platform asks nobody until somebody decides
/// it should. That mirrors <c>PaymentOptions.NoProvider</c> — a capability the platform does not
/// have is absent rather than simulated.
/// </remarks>
public sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    /// <summary>OpenStreetMap's own reverse-geocoding service. Free, and strict about how it is used.</summary>
    public const string NominatimProvider = "Nominatim";

    /// <summary>Ask nobody. The form still works; the owner types the address.</summary>
    public const string NoProvider = "None";

    public string Provider { get; init; } = NoProvider;

    public string BaseUrl { get; init; } = "https://nominatim.openstreetmap.org/";

    /// <summary>
    /// Who is calling. Nominatim's usage policy REQUIRES this to identify the application, and an
    /// unidentified caller is blocked — so the API refuses to start rather than run into a ban that
    /// would look, from inside, like the feature simply never working.
    /// </summary>
    public string? UserAgent { get; init; }

    /// <summary>A form waits on this, so it fails fast rather than holding the screen.</summary>
    public int TimeoutSeconds { get; init; } = 5;

    /// <summary>
    /// The gap the provider's policy demands between calls — one per second for Nominatim, for the
    /// whole server rather than per user. Enforced in the adapter, because nothing else can: an
    /// inbound rate limit counts requests arriving, not requests leaving.
    /// </summary>
    public int MinIntervalMs { get; init; } = 1000;

    /// <summary>
    /// How long an answer stays good. The policy requires caching, and a building does not move.
    /// </summary>
    public int CacheHours { get; init; } = 24;
}
