using System.Globalization;
using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using MediatR;

namespace Khadra.Application.Dealers.UpdateProfile;

// Spec 4.1, the dealer page: name, description, map pin, opening hours, logo and cover. Owner-only,
// and deliberately NOT gated on the business being able to trade: an applicant sent back for
// clarification fixing their description is the main use of this screen.

/// <summary>One day's hours as entered: "HH:mm" local times, or closed.</summary>
public sealed record DayScheduleInput(string Day, bool IsClosed, string? OpensAt, string? ClosesAt);

public sealed record UpdateDealerProfileCommand(
    Id OwnerUserId,
    string BusinessName,
    string? Description,
    double Latitude,
    double Longitude,
    IReadOnlyList<DayScheduleInput> OperatingHours) : ICommand<Result<DealerProfileDto, Error>>;

/// <summary>Step one of a logo or cover upload: where to PUT the bytes.</summary>
public sealed record RequestBrandingUploadCommand(Id OwnerUserId, string Kind, string ContentType)
    : ICommand<Result<BrandingUploadDto, Error>>;

/// <summary>Step three: the bytes are there; make them the logo or the cover.</summary>
public sealed record SetBrandingCommand(Id OwnerUserId, string Kind, string StorageKey)
    : ICommand<Result<DealerProfileDto, Error>>;

public sealed record BrandingUploadDto(string UploadUrl, string StorageKey, DateTimeOffset ExpiresAt);

public static class BrandingKinds
{
    public const string Logo = "logo";
    public const string Cover = "cover";

    public static bool IsKnown(string? kind) =>
        string.Equals(kind, Logo, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, Cover, StringComparison.OrdinalIgnoreCase);
}

public sealed class UpdateDealerProfileCommandValidator : AbstractValidator<UpdateDealerProfileCommand>
{
    public UpdateDealerProfileCommandValidator()
    {
        RuleFor(command => command.BusinessName).NotEmpty().MaximumLength(150);
        RuleFor(command => command.Description).MaximumLength(2000);
        RuleFor(command => command.Latitude).InclusiveBetween(-90, 90);
        RuleFor(command => command.Longitude).InclusiveBetween(-180, 180);
        RuleFor(command => command.OperatingHours).NotNull().Must(hours => hours.Count == 7)
            .WithMessage("Opening hours must cover all seven days.");
        RuleForEach(command => command.OperatingHours).ChildRules(day =>
        {
            day.RuleFor(input => input.Day).NotEmpty().MaximumLength(9);
            day.RuleFor(input => input.OpensAt).MaximumLength(5);
            day.RuleFor(input => input.ClosesAt).MaximumLength(5);
        });
    }
}

public sealed class RequestBrandingUploadCommandValidator : AbstractValidator<RequestBrandingUploadCommand>
{
    public RequestBrandingUploadCommandValidator()
    {
        RuleFor(command => command.Kind).NotEmpty().MaximumLength(10);
        RuleFor(command => command.ContentType).NotEmpty().MaximumLength(100);
    }
}

public sealed class SetBrandingCommandValidator : AbstractValidator<SetBrandingCommand>
{
    public SetBrandingCommandValidator()
    {
        RuleFor(command => command.Kind).NotEmpty().MaximumLength(10);
        RuleFor(command => command.StorageKey).NotEmpty().MaximumLength(500);
    }
}

public sealed class DealerProfileHandlers(
    DealerMembershipResolver membership,
    IUploadTicketService uploads,
    IDocumentStorage storage,
    IDocumentPolicySettings policy,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<UpdateDealerProfileCommand, Result<DealerProfileDto, Error>>,
    IRequestHandler<RequestBrandingUploadCommand, Result<BrandingUploadDto, Error>>,
    IRequestHandler<SetBrandingCommand, Result<DealerProfileDto, Error>>
{
    /// <summary>Where branding lives. Its OWN prefix: dealers/{id}/ holds licence scans and owner IDs, which must never become public.</summary>
    public static string BrandingPrefix(Id dealerId) => $"dealer-branding/{dealerId.Value}/";

    public async Task<Result<DealerProfileDto, Error>> Handle(UpdateDealerProfileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await OwnedAsync(request.OwnerUserId, cancellationToken);
        if (owned.IsFailure)
            return owned.Error;
        var dealer = owned.Value;

        var name = BusinessName.Create(request.BusinessName);
        if (name.IsFailure)
            return name.Error;

        var location = GeoPoint.Create(request.Latitude, request.Longitude);
        if (location.IsFailure)
            return location.Error;

        var hours = ParseHours(request.OperatingHours);
        if (hours.IsFailure)
            return hours.Error;

        var updated = dealer.UpdateProfile(name.Value, location.Value, hours.Value, request.Description, dealer.CityId);
        if (updated.IsFailure)
            return updated.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DealerProfileDto.From(dealer, request.OwnerUserId);
    }

    public async Task<Result<BrandingUploadDto, Error>> Handle(RequestBrandingUploadCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await OwnedAsync(request.OwnerUserId, cancellationToken);
        if (owned.IsFailure)
            return owned.Error;

        if (!BrandingKinds.IsKnown(request.Kind))
            return DealerErrors.InvalidBrandingKind;
        if (!IsImage(request.ContentType) || !policy.AllowedContentTypes.Contains(request.ContentType, StringComparer.OrdinalIgnoreCase))
            return DealerErrors.InvalidBrandingType;

        var key = $"{BrandingPrefix(owned.Value.Id)}{request.Kind.ToLowerInvariant()}-{Guid.NewGuid():N}{Extension(request.ContentType)}";
        var ticket = uploads.Issue(key, request.ContentType, clock.UtcNow);
        return new BrandingUploadDto(ticket.UploadUrl, ticket.StorageKey, ticket.ExpiresAt);
    }

    public async Task<Result<DealerProfileDto, Error>> Handle(SetBrandingCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await OwnedAsync(request.OwnerUserId, cancellationToken);
        if (owned.IsFailure)
            return owned.Error;
        var dealer = owned.Value;

        if (!BrandingKinds.IsKnown(request.Kind))
            return DealerErrors.InvalidBrandingKind;

        // Both checks, as for evidence and car photos: the key must be this dealer's, and the bytes
        // must actually be there. Without them "I uploaded it" is a client's promise.
        if (!request.StorageKey.StartsWith(BrandingPrefix(dealer.Id), StringComparison.Ordinal))
            return DealerErrors.BrandingOutsideDealer;
        await using (var content = await storage.OpenAsync(request.StorageKey, cancellationToken))
        {
            if (content is null)
                return DealerErrors.BrandingNotUploaded;
        }

        var superseded = string.Equals(request.Kind, BrandingKinds.Logo, StringComparison.OrdinalIgnoreCase)
            ? dealer.SetLogo(request.StorageKey)
            : dealer.SetCover(request.StorageKey);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Only after the row is committed: deleting first would lose the old image if the save failed.
        if (superseded is not null && superseded != request.StorageKey)
            await storage.DeleteAsync(superseded, cancellationToken);

        return DealerProfileDto.From(dealer, request.OwnerUserId);
    }

    private async Task<Result<Dealer, Error>> OwnedAsync(Id ownerUserId, CancellationToken cancellationToken)
    {
        var member = await membership.ResolveAsync(ownerUserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;

        return member.Value.IsOwner ? member.Value.Dealer : DealerErrors.OwnerOnly;
    }

    /// <summary>Seven rows into the domain's per-day schedule; any unparseable row is the domain's invalid-hours error.</summary>
    private static Result<OperatingHours, Error> ParseHours(IReadOnlyList<DayScheduleInput> inputs)
    {
        var schedules = new List<DaySchedule>(7);
        foreach (var input in inputs)
        {
            if (!Enum.TryParse<DayOfWeek>(input.Day, ignoreCase: true, out var day))
                return DealerErrors.InvalidOperatingHours;

            if (input.IsClosed)
            {
                schedules.Add(DaySchedule.Closed(day));
                continue;
            }

            if (!TimeOnly.TryParseExact(input.OpensAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var opens) ||
                !TimeOnly.TryParseExact(input.ClosesAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var closes))
            {
                return DealerErrors.InvalidOperatingHours;
            }

            var open = DaySchedule.Open(day, opens, closes);
            if (open.IsFailure)
                return open.Error;
            schedules.Add(open.Value);
        }

        return OperatingHours.Create(schedules);
    }

    private static bool IsImage(string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static string Extension(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
}
