using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.PlatformSettings;
using Khadra.Domain.PlatformSettings.Repositories;
using MediatR;

namespace Khadra.Application.Dealers.SuggestAddress;

/// <summary>
/// What a map pin might be called, offered to the form the owner is filling in.
/// </summary>
/// <param name="Area">The neighbourhood, when the provider knows one.</param>
/// <param name="Street">The road, when it has a recorded name.</param>
/// <param name="CityName">What the provider calls the settlement — for display, not for storage.</param>
/// <param name="SuggestedCityId">
/// A curated city whose name matches, when EXACTLY one does. Null when none match or several do,
/// because a wrong pre-selection is worse than none: the owner would have to notice it to fix it.
/// </param>
/// <param name="Attribution">The provider's licence line, which the screen prints beside the fields.</param>
public sealed record AddressSuggestionDto(
    string? Area,
    string? Street,
    string? CityName,
    Guid? SuggestedCityId,
    string Attribution);

/// <summary>
/// Asks the geocoding proxy what is at a point. A READ, and never part of saving anything.
/// </summary>
public sealed record SuggestDealerAddressQuery(double Latitude, double Longitude, string Language)
    : IQuery<Result<AddressSuggestionDto, Error>>;

public sealed class SuggestDealerAddressQueryValidator : AbstractValidator<SuggestDealerAddressQuery>
{
    public SuggestDealerAddressQueryValidator()
    {
        RuleFor(query => query.Latitude).InclusiveBetween(-90, 90);
        RuleFor(query => query.Longitude).InclusiveBetween(-180, 180);
        RuleFor(query => query.Language).MaximumLength(10);
    }
}

public sealed class SuggestDealerAddressHandler(IReverseGeocoder geocoder, ICityRepository cities)
    : IRequestHandler<SuggestDealerAddressQuery, Result<AddressSuggestionDto, Error>>
{
    public async Task<Result<AddressSuggestionDto, Error>> Handle(
        SuggestDealerAddressQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var point = GeoPoint.Create(request.Latitude, request.Longitude);
        if (point.IsFailure)
            return point.Error;

        var found = await geocoder.LookupAsync(point.Value, request.Language, cancellationToken);
        // Unavailable and NotFound both travel out untouched. The form distinguishes them: one says
        // the service is down, the other says there is nothing recorded at that spot, and both end
        // with the owner typing the address.
        if (found.IsFailure)
            return found.Error;

        var suggestion = found.Value;
        var cityId = await MatchCityAsync(suggestion.City, cancellationToken);

        return new AddressSuggestionDto(
            suggestion.Area,
            suggestion.Street,
            suggestion.City,
            cityId,
            suggestion.Attribution);
    }

    /// <summary>
    /// Matches the provider's settlement name against the curated cities, in either language.
    /// </summary>
    /// <remarks>
    /// Only an unambiguous single match counts. Two cities sharing a comparison key is a curation
    /// problem an administrator should see, not something to resolve by guessing; and the id is only
    /// ever a PRE-SELECTION in a dropdown the owner can change, so the cost of returning nothing is
    /// one extra click while the cost of choosing wrongly is a gallery filed under the wrong city.
    ///
    /// ComparisonKey is the lookup's own comparison, which strips tashkeel and tatweel, so an Arabic
    /// name written with vowel marks still matches the curated row.
    /// </remarks>
    private async Task<Guid?> MatchCityAsync(string? cityName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cityName)) return null;

        var key = LookupEntry.ComparisonKey(cityName);
        if (key.Length == 0) return null;

        var active = await cities.ListAsync(activeOnly: true, cancellationToken);
        var matches = active
            .Where(city =>
                LookupEntry.ComparisonKey(city.NameEn) == key ||
                LookupEntry.ComparisonKey(city.NameAr) == key)
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0].Id.Value : null;
    }
}
