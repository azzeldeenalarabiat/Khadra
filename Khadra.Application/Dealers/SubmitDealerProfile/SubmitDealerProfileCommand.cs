using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using MediatR;

namespace Khadra.Application.Dealers.SubmitDealerProfile;

/// <summary>One of the commercial documents spec 3.1 requires, still as a stream from the transport.</summary>
public sealed record DealerDocumentUpload(
    string DocumentType,
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content);

/// <summary>
/// Step two of spec 3.1: the business itself is submitted for the Admin's licence check.
///
/// The dealer is created PENDING_REVIEW and cannot trade until an Admin approves it. Operating hours
/// are taken as one open/close window applied to every day: the spec asks for "operating hours"
/// without saying whether they vary by day, and a uniform week is the honest minimum until the dealer
/// profile editor exists to express more.
/// </summary>
public sealed record SubmitDealerProfileCommand(
    Id OwnerUserId,
    string BusinessName,
    string CommercialRegistrationNumber,
    double Latitude,
    double Longitude,
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    string? Description,
    Id? CityId,
    IReadOnlyList<DealerDocumentUpload> Documents) : ICommand<Result<DealerProfileDto, Error>>;

public sealed class SubmitDealerProfileCommandValidator : AbstractValidator<SubmitDealerProfileCommand>
{
    public SubmitDealerProfileCommandValidator()
    {
        RuleFor(command => command.BusinessName).NotEmpty().MaximumLength(BusinessName.MaxLength);
        // Raw input, so it has room for the separators the value object strips. Capping it at the
        // DIGIT maximum would refuse a legitimate 20-digit number typed with dashes.
        RuleFor(command => command.CommercialRegistrationNumber)
            .NotEmpty().MaximumLength(CommercialRegistrationNumber.MaxLength + 10);
        RuleFor(command => command.Latitude).InclusiveBetween(-90, 90);
        RuleFor(command => command.Longitude).InclusiveBetween(-180, 180);
        RuleFor(command => command.Description).MaximumLength(2000);
        RuleFor(command => command.Documents).NotEmpty();
    }
}

public sealed class SubmitDealerProfileHandler(
    IDealerRepository dealers,
    IDocumentStorage storage,
    IDocumentPolicySettings policy,
    IBusinessRulesProvider businessRules,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SubmitDealerProfileCommand, Result<DealerProfileDto, Error>>
{
    public async Task<Result<DealerProfileDto, Error>> Handle(
        SubmitDealerProfileCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // One dealer per owner. Re-submitting a rejected application is Dealer.Resubmit, a different
        // use case, so this refuses rather than silently creating a second business.
        if (await dealers.ExistsForOwnerAsync(request.OwnerUserId, cancellationToken))
            return DealerErrors.AlreadyRegistered;

        var businessName = BusinessName.Create(request.BusinessName);
        if (businessName.IsFailure)
            return businessName.Error;

        var registration = CommercialRegistrationNumber.Create(request.CommercialRegistrationNumber);
        if (registration.IsFailure)
            return registration.Error;

        if (await dealers.CommercialRegistrationExistsAsync(registration.Value, cancellationToken))
            return DealerErrors.CommercialRegistrationTaken;

        var location = GeoPoint.Create(request.Latitude, request.Longitude);
        if (location.IsFailure)
            return location.Error;

        var hours = OperatingHours.Uniform(request.OpensAt, request.ClosesAt);
        if (hours.IsFailure)
            return hours.Error;

        var uploads = ResolveUploads(request.Documents);
        if (uploads.IsFailure)
            return uploads.Error;

        // Completeness is checked BEFORE anything is written. Discovering a missing document after
        // saving the other two would leave orphaned files in storage that no record points at, and
        // sensitive documents that nothing will ever clean up are the worst kind of litter.
        var provided = uploads.Value.Select(upload => upload.Type).ToList();
        if (DealerDocumentType.Required.Any(required => !provided.Contains(required)))
            return DealerErrors.MissingRequiredDocuments;

        var rules = await businessRules.GetAsync(cancellationToken);
        var now = clock.UtcNow;

        var dealer = Dealer.Register(
            request.OwnerUserId,
            businessName.Value,
            registration.Value,
            location.Value,
            hours.Value,
            now,
            TimeSpan.FromHours(rules.AdminSlaHours),
            request.Description,
            request.CityId);

        foreach (var (type, upload) in uploads.Value)
        {
            var stored = await storage.SaveAsync(
                $"dealers/{dealer.Id.Value}",
                upload.FileName,
                upload.ContentType,
                upload.Content,
                cancellationToken);

            var attached = dealer.AttachDocument(type, stored.StorageKey, now);
            if (attached.IsFailure)
                return attached.Error;
        }

        // The aggregate has the final say; the pre-check above only spares storage the wasted writes.
        if (!dealer.HasAllRequiredDocuments)
            return DealerErrors.MissingRequiredDocuments;

        await dealers.AddAsync(dealer, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DealerProfileDto.From(dealer);
    }

    private Result<IReadOnlyList<(DealerDocumentType Type, DealerDocumentUpload Upload)>, Error> ResolveUploads(
        IReadOnlyList<DealerDocumentUpload> uploads)
    {
        var resolved = new List<(DealerDocumentType, DealerDocumentUpload)>();
        foreach (var upload in uploads)
        {
            var type = Enumeration.GetAll<DealerDocumentType>()
                .SingleOrDefault(candidate =>
                    string.Equals(candidate.Name, upload.DocumentType, StringComparison.OrdinalIgnoreCase));
            if (type is null)
                return DealerErrors.UnsupportedDocumentType;

            if (upload.SizeBytes <= 0 || upload.SizeBytes > policy.MaximumSizeBytes)
                return DealerErrors.DocumentTooLarge;

            if (!policy.AllowedContentTypes.Contains(upload.ContentType, StringComparer.OrdinalIgnoreCase))
                return DealerErrors.InvalidDocumentContent;

            resolved.Add((type, upload));
        }

        return resolved;
    }
}
