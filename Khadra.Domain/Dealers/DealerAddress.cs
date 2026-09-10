using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Dealers;

/// <summary>
/// Where a gallery is, in words: the area it is in, and the street when the street has a name.
/// </summary>
/// <remarks>
/// The PIN is the authoritative location — every distance, every delivery-radius decision and the
/// catalogue's own geography run on <see cref="GeoPoint"/>. This is what a person reads, and what a
/// customer collecting a car in Abdoun actually needs, because "31.95, 35.91" gets nobody to a door.
///
/// The area is required and the street is not, which is the honest way round for Jordan: "Abdoun"
/// alone gets a customer to the right part of Amman, "Al-Kindi St" alone does not, and a great many
/// streets have no name recorded anywhere.
///
/// A map pin can OFFER these two values through the reverse-geocoding suggestion endpoint, but what
/// is stored is what the owner confirmed in the form. Nothing on the write path calls a geocoder:
/// its answers for Jordan vary between suburb, neighbourhood and city district, sometimes give only
/// "Amman", and this is a record an administrator checks against a commercial licence. A third
/// party's guess is not the owner's statement about their own business.
///
/// No guards in the constructor, following <see cref="GeoPoint"/> and <c>Money</c>: EF binds these
/// properties directly when it reads a row, so a rule tightened later must not make an existing row
/// unreadable. Validation lives in <see cref="Create"/>, which is the only door a write comes through.
/// </remarks>
public sealed partial class DealerAddress : ValueObject
{
    /// <summary>Matches the city name column, because an area name is the same kind of thing.</summary>
    public const int AreaMaxLength = 100;

    public const int StreetMaxLength = 200;

    public string Area { get; }

    public string? Street { get; }

    private DealerAddress(string area, string? street)
    {
        Area = area;
        Street = street;
    }

    /// <summary>Rebuilds a stored address, taken as-is.</summary>
    /// <remarks>
    /// Reading a row is not the moment to re-litigate whether it should have been allowed in. The
    /// same reasoning as <c>EmailAddress.FromPersisted</c>: tightening a rule must not stop a
    /// gallery's own page loading.
    /// </remarks>
    public static DealerAddress FromPersisted(string area, string? street) => new(area, street);

    /// <summary>
    /// An address the owner has stated. An empty street is stored as nothing at all rather than as
    /// an empty string, so "no street recorded" is one value and not two.
    /// </summary>
    public static Result<DealerAddress, Error> Create(string? area, string? street)
    {
        if (!TryTidy(area, out var cleanArea) || cleanArea is null)
            return DealerErrors.InvalidAddressArea;
        if (cleanArea.Length > AreaMaxLength)
            return DealerErrors.AddressAreaTooLong;

        // Absent and unusable are two different answers, and collapsing them would lose a street
        // the owner typed without telling them. Absent is fine; unusable is refused.
        if (!TryTidy(street, out var cleanStreet))
            return DealerErrors.InvalidAddressStreet;
        // Refused rather than truncated, unlike the description. A description cut short is still
        // about the same business; a street name cut short is a different street.
        if (cleanStreet is not null && cleanStreet.Length > StreetMaxLength)
            return DealerErrors.AddressStreetTooLong;

        return new DealerAddress(cleanArea, cleanStreet);
    }

    /// <summary>
    /// Trims and collapses whitespace runs. Returns false when the text is PRESENT but unusable —
    /// a control character in it — and true with a null value when it is simply absent.
    /// </summary>
    private static bool TryTidy(string? raw, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        var collapsed = WhitespaceRun().Replace(raw.Trim(), " ");
        if (collapsed.Any(char.IsControl)) return false;

        value = collapsed;
        return true;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Area;
        yield return Street;
    }

    public override string ToString() => Street is null ? Area : $"{Street}, {Area}";
}
