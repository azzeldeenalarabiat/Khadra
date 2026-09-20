using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using MediatR;

namespace Khadra.Application.Dealers.CustomerPage;

// What a rental office tells its customers in its own words, and which of it it shows.
//
// Owner-only to write, and deliberately NOT gated on the business being able to trade: an applicant
// waiting on approval prepares the page, and nothing on it reaches a customer until the office may
// trade — the public endpoint will not return a gallery that cannot.
//
// The platform's own rules are not here and never will be. Cancellation, payment, the deposit and the
// documents a renter needs are the platform's, stated on every booking quote; fuel, mileage and the
// security deposit are the car's, set per vehicle in Fleet. An office writing its own version of any
// of them would be writing a promise nothing enforces.

/// <summary>The page as its owner and staff see it in the console.</summary>
public sealed record GetDealerCustomerPageQuery(Id UserId) : IQuery<Result<DealerCustomerPageDto, Error>>;

/// <summary>
/// The whole page as the owner submitted it.
/// </summary>
/// <remarks>
/// A full replacement, not a patch: the console sends every section, so one left out is one cleared.
/// A patch would leave text the owner can no longer see on a page customers can.
/// </remarks>
public sealed record UpdateDealerCustomerPageCommand(
    Id OwnerUserId,
    string? About,
    string? RentalConditions,
    string? Insurance,
    string? PickupInstructions,
    string? DeliveryNotes,
    string? CustomerNotes,
    IReadOnlyList<string>? HiddenSections) : ICommand<Result<DealerCustomerPageDto, Error>>;

/// <summary>
/// The editor's whole state: what is written, what is hidden, what MAY be hidden, and what a customer
/// would see.
/// </summary>
/// <remarks>
/// <para><see cref="Sections"/> is the vocabulary the console builds its toggles from, so a section the
/// platform adds appears in the editor without a console release — and a console cannot invent one
/// the platform does not have.</para>
/// <para><see cref="Visible"/> is the server's own answer to "what does a customer see", from the same
/// call the public page makes. The preview therefore cannot drift from the page, which a console
/// re-implementing "hidden or empty" for itself certainly would.</para>
/// <para><see cref="DeliveryEnabled"/> is here because delivery notes are not shown while delivery is
/// off, and an owner typing into a box that shows nothing deserves to be told why.</para>
/// </remarks>
public sealed record DealerCustomerPageDto(
    string? About,
    string? RentalConditions,
    string? Insurance,
    string? PickupInstructions,
    string? DeliveryNotes,
    string? CustomerNotes,
    IReadOnlyList<string> HiddenSections,
    IReadOnlyList<string> Sections,
    int MaxTextLength,
    bool DeliveryEnabled,
    VisibleCustomerPageDto Visible)
{
    public static DealerCustomerPageDto From(Dealer dealer)
    {
        ArgumentNullException.ThrowIfNull(dealer);

        var page = dealer.PublicProfile;
        var shown = dealer.VisiblePublicProfile();

        return new DealerCustomerPageDto(
            dealer.Description,
            page.RentalConditions,
            page.Insurance,
            page.PickupInstructions,
            page.DeliveryNotes,
            page.CustomerNotes,
            [.. page.HiddenSections.OrderBy(section => section.Id).Select(section => section.Name)],
            [.. Enumeration.GetAll<PublicProfileSection>().Select(section => section.Name)],
            ProfileText.MaxLength,
            dealer.Delivery.IsEnabled,
            new VisibleCustomerPageDto(
                shown.About,
                shown.RentalConditions,
                shown.Insurance,
                shown.PickupInstructions,
                shown.DeliveryNotes,
                shown.CustomerNotes));
    }
}

/// <summary>What a customer would see of the page. Null is "nothing to show", with no reason given.</summary>
public sealed record VisibleCustomerPageDto(
    string? About,
    string? RentalConditions,
    string? Insurance,
    string? PickupInstructions,
    string? DeliveryNotes,
    string? CustomerNotes);

/// <summary>
/// Length and characters, per field, so the console can put each message under the box that caused it.
/// </summary>
/// <remarks>
/// The domain holds the same two rules and is the authority — it refuses rather than cuts, and it is
/// what a second caller would meet. These exist so six bad boxes come back as six field messages
/// rather than as one sentence about a form.
/// </remarks>
public sealed class UpdateDealerCustomerPageCommandValidator : AbstractValidator<UpdateDealerCustomerPageCommand>
{
    private const string Unshowable = "This section contains characters that cannot be shown.";

    public UpdateDealerCustomerPageCommandValidator()
    {
        RuleFor(command => command.About).MaximumLength(ProfileText.MaxLength)
            .Must(ProfileText.HasOnlyAllowedCharacters).WithMessage(Unshowable);
        RuleFor(command => command.RentalConditions).MaximumLength(ProfileText.MaxLength)
            .Must(ProfileText.HasOnlyAllowedCharacters).WithMessage(Unshowable);
        RuleFor(command => command.Insurance).MaximumLength(ProfileText.MaxLength)
            .Must(ProfileText.HasOnlyAllowedCharacters).WithMessage(Unshowable);
        RuleFor(command => command.PickupInstructions).MaximumLength(ProfileText.MaxLength)
            .Must(ProfileText.HasOnlyAllowedCharacters).WithMessage(Unshowable);
        RuleFor(command => command.DeliveryNotes).MaximumLength(ProfileText.MaxLength)
            .Must(ProfileText.HasOnlyAllowedCharacters).WithMessage(Unshowable);
        RuleFor(command => command.CustomerNotes).MaximumLength(ProfileText.MaxLength)
            .Must(ProfileText.HasOnlyAllowedCharacters).WithMessage(Unshowable);

        // No more names than there are sections, and none longer than one could be. Which names are
        // real is the domain's answer, and it refuses an unknown one rather than ignoring it.
        RuleFor(command => command.HiddenSections)
            .Must(names => names is null || names.Count <= Enumeration.GetAll<PublicProfileSection>().Count)
            .WithMessage("That is more sections than the page has.");
        RuleForEach(command => command.HiddenSections).NotEmpty().MaximumLength(40);
    }
}

public sealed class DealerCustomerPageHandlers(
    DealerMembershipResolver membership,
    IUnitOfWork unitOfWork) :
    IRequestHandler<GetDealerCustomerPageQuery, Result<DealerCustomerPageDto, Error>>,
    IRequestHandler<UpdateDealerCustomerPageCommand, Result<DealerCustomerPageDto, Error>>
{
    /// <summary>Staff may read the page they answer bookings for; only the owner writes it.</summary>
    public async Task<Result<DealerCustomerPageDto, Error>> Handle(
        GetDealerCustomerPageQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var member = await membership.ResolveAsync(request.UserId, cancellationToken);
        return member.IsFailure ? member.Error : DealerCustomerPageDto.From(member.Value.Dealer);
    }

    public async Task<Result<DealerCustomerPageDto, Error>> Handle(
        UpdateDealerCustomerPageCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var member = await membership.ResolveAsync(request.OwnerUserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;

        // Spec 4.2: what the platform tells customers about this office is the owner's to decide.
        if (!member.Value.IsOwner)
            return DealerErrors.OwnerOnly;

        var page = Domain.Dealers.PublicProfile.Create(
            request.RentalConditions,
            request.Insurance,
            request.PickupInstructions,
            request.DeliveryNotes,
            request.CustomerNotes,
            request.HiddenSections);
        if (page.IsFailure)
            return page.Error;

        var dealer = member.Value.Dealer;
        var updated = dealer.UpdatePublicProfile(request.About, page.Value);
        if (updated.IsFailure)
            return updated.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DealerCustomerPageDto.From(dealer);
    }
}
