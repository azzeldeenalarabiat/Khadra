using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Application.Common.Ports;

/// <summary>
/// What a provider says is at a point on the map. Every field is a SUGGESTION, never a record.
/// </summary>
/// <param name="Area">The neighbourhood or district. Providers disagree about which field this is.</param>
/// <param name="Street">The road, when it has a recorded name. A great many in Jordan do not.</param>
/// <param name="City">The settlement, matched against the platform's curated cities by the handler.</param>
/// <param name="Attribution">
/// The provider's own licence line, carried out so the screen can print it. Open data licences
/// require attribution where the data is shown, and a literal in a template would be a claim about
/// where this came from that stops being true the day the provider changes.
/// </param>
/// <param name="Language">The language the provider answered in, which may not be the one asked for.</param>
public sealed record ReverseGeocodeResult(
    string? Area,
    string? Street,
    string? City,
    string Attribution,
    string Language);

/// <summary>
/// Turns a map pin into words, for a form to offer to the person filling it in.
/// </summary>
/// <remarks>
/// NOTHING ON A WRITE PATH MAY CALL THIS. It exists to pre-fill a form; what is stored is what the
/// owner confirmed. Three reasons, and the first two are the practical ones:
///
///   * providers rate-limit hard — Nominatim allows roughly one request per second for an entire
///     server and may withdraw access without notice — so a submit that waits on one is a submit
///     that fails whenever the provider is busy;
///   * for Jordan the answers are inconsistent between suburb, neighbourhood and city district, and
///     sometimes give only "Amman", so an unreviewed value is a third party's guess written onto the
///     record an administrator checks against a commercial licence;
///   * the platform's standing rule is that data is created through the screens. A suggestion the
///     owner accepted is their statement; a value written behind them is not.
///
/// It never invents. A point it cannot resolve is <see cref="ErrorKind.NotFound"/>, and a provider
/// that is unreachable, throttled or unconfigured is <see cref="ErrorKind.Unavailable"/> — the form
/// then says so and the owner types it themselves, which is the same thing they did before this
/// existed.
/// </remarks>
public interface IReverseGeocoder
{
    /// <summary>False when no provider is configured, so callers can say "type it" without a round trip.</summary>
    bool IsConfigured { get; }

    Task<Result<ReverseGeocodeResult, Error>> LookupAsync(
        GeoPoint point,
        string language,
        CancellationToken cancellationToken = default);
}
