using Khadra.Application.Common.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;

namespace Khadra.Application.Dealers.Dtos;

/// <summary>
/// A dealer as its own owner or staff see it.
///
/// `CanTrade` is included even though it is derivable from the status, because it is the question
/// every client actually asks and the domain already answers it (approved AND not suspended AND not
/// deleted). Letting a client recompute that from a status string is how the three conditions get
/// forgotten one at a time.
/// </summary>
/// <summary>Where the gallery is in words, as the owner recorded it. Null when none is recorded.</summary>
/// <remarks>
/// Nested rather than two more top-level fields, so "there is no address" is one null instead of a
/// pair a reader has to interpret. The pin beside it stays the authoritative location.
/// </remarks>
public sealed record DealerAddressDto(string Area, string? Street);

public sealed record DealerProfileDto(
    Guid DealerId,
    string BusinessName,
    string CommercialRegistrationNumber,
    string VerificationStatus,
    string? ReviewNote,
    string? SuspensionReason,
    DateTimeOffset SubmittedAt,
    DateTimeOffset ReviewDueAt,
    DateTimeOffset CreatedAt,
    bool CanTrade,
    bool IsSuspended,
    IReadOnlyList<string> SubmittedDocuments,
    IReadOnlyList<string> MissingDocuments,
    // Every document type an approval requires (spec 3.1). Sent rather than counted in the browser:
    // the console printed "of 3" in prose, so adding a fourth required type would have left it
    // telling an Admin the application was complete while approval kept failing.
    IReadOnlyList<string> RequiredDocuments,
    // Spec 4.1: what the dealer page shows. Editable by the owner, read by everyone else.
    string? Description,
    double Latitude,
    double Longitude,
    IReadOnlyList<DayScheduleDto> OperatingHours,
    DeliverySettingsDto Delivery,
    // Public, cacheable paths (spec 4.1 shows logo and cover to every customer); null until set.
    string? LogoUrl,
    string? CoverUrl,
    int EmployeeCount,
    // Who is asking. The console decides which controls to show from these, but every dealer
    // endpoint enforces the same answer server-side; these are hints, not permissions.
    bool IsOwner,
    bool CanViewReports,
    // The curated city row this gallery is filed under, and the address in words. Both optional:
    // a gallery registered before either existed has neither, and nothing invents one.
    Guid? CityId,
    DealerAddressDto? Address)
{
    public static DealerProfileDto From(Dealer dealer, Id? actorUserId = null)
    {
        ArgumentNullException.ThrowIfNull(dealer);

        var held = dealer.Documents.Select(document => document.Type).ToList();
        var isOwner = actorUserId is { } actor && dealer.OwnerUserId == actor;
        return new DealerProfileDto(
            dealer.Id.Value,
            dealer.BusinessName.Value,
            dealer.CommercialRegistration.Value,
            dealer.VerificationStatus.Name,
            dealer.ReviewNote,
            dealer.SuspensionReason,
            dealer.SubmittedAt,
            dealer.ReviewDueAt,
            dealer.CreatedAt,
            dealer.CanTrade,
            dealer.IsSuspended,
            [.. held.OrderBy(type => type.Id).Select(type => type.Name)],
            [.. DealerDocumentType.Required.Where(required => !held.Contains(required)).Select(type => type.Name)],
            [.. DealerDocumentType.Required.Select(type => type.Name)],
            dealer.Description,
            dealer.Location.Latitude,
            dealer.Location.Longitude,
            [.. dealer.OperatingHours.Days.Select(DayScheduleDto.From)],
            new DeliverySettingsDto(
                dealer.Delivery.IsEnabled,
                dealer.Delivery.RadiusKm,
                MoneyDto.FromOptional(dealer.Delivery.Fee)),
            PublicImage(dealer.LogoStorageKey),
            PublicImage(dealer.CoverStorageKey),
            dealer.Employees.Count(employee => employee.IsActive),
            isOwner,
            actorUserId is { } viewer && dealer.CanViewReports(viewer),
            dealer.CityId?.Value,
            dealer.Address is null ? null : new DealerAddressDto(dealer.Address.Area, dealer.Address.Street));
    }

    /// <summary>The anonymous, cacheable path DealerImagesController serves branding from.</summary>
    public const string PublicImagePath = "/api/v1/dealer-images";

    private static string? PublicImage(string? storageKey) =>
        storageKey is null ? null : $"{PublicImagePath}/{storageKey}";
}

/// <summary>One day of the week. Times are the dealer's local opening times, as entered.</summary>
public sealed record DayScheduleDto(string Day, bool IsClosed, string? OpensAt, string? ClosesAt)
{
    public static DayScheduleDto From(DaySchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return new DayScheduleDto(
            schedule.Day.ToString(),
            schedule.IsClosed,
            schedule.IsClosed ? null : schedule.OpensAt.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            schedule.IsClosed ? null : schedule.ClosesAt.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <param name="Fee">
/// What this gallery charges to deliver, null exactly when delivery is off. Each gallery sets its
/// own; there is no platform figure behind it.
/// </param>
public sealed record DeliverySettingsDto(bool IsEnabled, decimal RadiusKm, MoneyDto? Fee);
