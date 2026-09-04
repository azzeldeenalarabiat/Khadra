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
    bool CanViewReports)
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
            new DeliverySettingsDto(dealer.Delivery.IsEnabled, dealer.Delivery.RadiusKm),
            PublicImage(dealer.LogoStorageKey),
            PublicImage(dealer.CoverStorageKey),
            dealer.Employees.Count(employee => employee.IsActive),
            isOwner,
            actorUserId is { } viewer && dealer.CanViewReports(viewer));
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

public sealed record DeliverySettingsDto(bool IsEnabled, decimal RadiusKm);
