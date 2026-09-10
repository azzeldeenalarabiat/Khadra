using System.Globalization;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Geocoding;

/// <summary>
/// Asks OpenStreetMap's Nominatim what is at a point, within the manners its usage policy requires.
/// </summary>
/// <remarks>
/// Three obligations, and breaking any of them gets the platform's address blocked rather than
/// throttled — which fails invisibly, for everyone, until somebody notices every suggestion is a 403:
///
///   * identify the caller. The User-Agent must name the application; the default one is refused.
///     Checked at STARTUP (see DependencyInjection), because a ban is not a degraded state.
///   * at most one request per second, for the whole server. Enforced here with a semaphore and a
///     timestamp, because nothing upstream can: an inbound rate limiter counts requests arriving.
///   * cache the answers. A building does not move, so a repeat question is answered from memory.
///
/// A caller that would have to WAIT longer than its own budget is told Unavailable immediately
/// instead of queueing. A form is on the other end of this, and a person watching a spinner for four
/// seconds has already started typing the address themselves.
///
/// Only ever a reverse lookup: latitude, longitude, and a whitelisted language. No free-text search,
/// which is forward geocoding and which the policy forbids driving from a keystroke. The caller's own
/// address and user agent are never forwarded — the provider learns a coordinate, not a person.
/// </remarks>
internal sealed class NominatimReverseGeocoder : IReverseGeocoder, IDisposable
{
    public const string HttpClientName = "nominatim";

    /// <summary>Whitelisted, because this value goes into an outbound query string.</summary>
    private static readonly string[] SupportedLanguages = ["en", "ar"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly GeocodingOptions _options;

    /// <summary>One at a time, so the one-per-second promise is kept across concurrent callers.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset _lastCall = DateTimeOffset.MinValue;

    public NominatimReverseGeocoder(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<GeocodingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options.Value;
    }

    public bool IsConfigured => true;

    public async Task<Result<ReverseGeocodeResult, Error>> LookupAsync(
        GeoPoint point,
        string language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(point);

        var tongue = SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase)
            ? language.ToLowerInvariant()
            : SupportedLanguages[0];

        // Rounded to about eleven metres before it becomes a cache key. The pin carries six decimals,
        // so without this a one-metre nudge of the map would be a fresh question to the provider and
        // the cache would never hit.
        var key = $"geo:{point.Latitude:0.0000},{point.Longitude:0.0000}:{tongue}";
        if (_cache.TryGetValue(key, out ReverseGeocodeResult? cached) && cached is not null)
            return cached;

        var budget = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        var admitted = await TryEnterAsync(budget, cancellationToken);
        if (admitted.IsFailure)
            return admitted.Error;

        try
        {
            var result = await CallAsync(point, tongue, cancellationToken);
            if (result.IsSuccess)
            {
                _cache.Set(key, result.Value, TimeSpan.FromHours(_options.CacheHours));
            }

            return result;
        }
        finally
        {
            _lastCall = DateTimeOffset.UtcNow;
            _gate.Release();
        }
    }

    /// <summary>
    /// Takes the outbound slot, waiting out the provider's minimum interval — but only if the wait
    /// fits inside this caller's own timeout. Otherwise it refuses now rather than late.
    /// </summary>
    private async Task<UnitResult<Error>> TryEnterAsync(TimeSpan budget, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(budget, cancellationToken))
            return GeocodingErrors.Busy;

        var sinceLast = DateTimeOffset.UtcNow - _lastCall;
        var wait = TimeSpan.FromMilliseconds(_options.MinIntervalMs) - sinceLast;
        if (wait <= TimeSpan.Zero)
            return UnitResult.Success<Error>();

        if (wait >= budget)
        {
            // Releasing here matters: the finally in LookupAsync only runs on the path that entered.
            _gate.Release();
            return GeocodingErrors.Busy;
        }

        await Task.Delay(wait, cancellationToken);
        return UnitResult.Success<Error>();
    }

    private async Task<Result<ReverseGeocodeResult, Error>> CallAsync(
        GeoPoint point,
        string language,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var query =
            $"reverse?format=jsonv2&addressdetails=1&zoom=18" +
            $"&lat={point.Latitude.ToString("0.######", CultureInfo.InvariantCulture)}" +
            $"&lon={point.Longitude.ToString("0.######", CultureInfo.InvariantCulture)}" +
            $"&accept-language={language}";

        try
        {
            using var response = await client.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return GeocodingErrors.Unavailable;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            // Nominatim answers a point it has nothing for with an `error` member and HTTP 200, so a
            // successful status is not by itself an answer.
            if (root.TryGetProperty("error", out _))
                return GeocodingErrors.NoAddressThere;

            if (!root.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object)
                return GeocodingErrors.NoAddressThere;

            // Which member holds the neighbourhood varies by country and by how well mapped the spot
            // is, so they are tried in the order that is most specific first.
            var area = First(address, "suburb", "neighbourhood", "city_district", "quarter", "residential");
            var street = First(address, "road", "pedestrian", "footway");
            var city = First(address, "city", "town", "village", "municipality", "state");

            if (area is null && street is null && city is null)
                return GeocodingErrors.NoAddressThere;

            var attribution = root.TryGetProperty("licence", out var licence)
                ? licence.GetString() ?? OpenStreetMapLicence
                : OpenStreetMapLicence;

            return new ReverseGeocodeResult(area, street, city, attribution, language);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout, not the caller giving up.
            return GeocodingErrors.Unavailable;
        }
        catch (HttpRequestException)
        {
            return GeocodingErrors.Unavailable;
        }
        catch (JsonException)
        {
            // A body that is not the JSON promised is the provider misbehaving, not a missing address.
            return GeocodingErrors.Unavailable;
        }
    }

    private const string OpenStreetMapLicence =
        "Data © OpenStreetMap contributors, ODbL 1.0. https://osm.org/copyright";

    private static string? First(JsonElement address, params string[] names)
    {
        foreach (var name in names)
        {
            if (address.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString();
            }
        }

        return null;
    }

    public void Dispose() => _gate.Dispose();
}

/// <summary>The answer when no provider is configured: the form asks the owner to type it.</summary>
internal sealed class UnconfiguredReverseGeocoder : IReverseGeocoder
{
    public bool IsConfigured => false;

    public Task<Result<ReverseGeocodeResult, Error>> LookupAsync(
        GeoPoint point,
        string language,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<ReverseGeocodeResult, Error>(GeocodingErrors.NotConfigured));
}

internal static class GeocodingErrors
{
    public static readonly Error NotConfigured = Error.Unavailable(
        "geocoding.not_configured",
        "Address suggestions are not switched on. Type the area and street yourself.");

    public static readonly Error Unavailable = Error.Unavailable(
        "geocoding.unavailable",
        "The address service did not answer. Type the area and street yourself.");

    public static readonly Error Busy = Error.Unavailable(
        "geocoding.busy",
        "The address service is rate limited just now. Try again in a moment, or type it yourself.");

    public static readonly Error NoAddressThere = Error.NotFound(
        "geocoding.no_address_there",
        "No address is recorded at that point. Type the area and street yourself.");
}
