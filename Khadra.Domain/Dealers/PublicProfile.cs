using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Dealers;

/// <summary>
/// What a rental office writes for its customers beyond what the platform already shows, and which of
/// it the office has chosen to hide.
/// </summary>
/// <remarks>
/// <para>
/// The office's OWN words, labelled as such wherever they appear: its rental conditions, its insurance,
/// how a pickup works at its counter, what to know about its delivery, and anything else it wants
/// customers told. None of it is enforced at booking and none of it is frozen onto one — a booking is
/// governed by the terms and the price it froze, never by this.
/// </para>
/// <para>
/// The About text is not here. It is <see cref="Dealer.Description"/>, which predates this and has
/// been the office's description since registration; its visibility is still decided here, alongside
/// the others, in <see cref="HiddenSections"/>.
/// </para>
/// </remarks>
public sealed class PublicProfile : ValueObject
{
    private PublicProfile(
        string? rentalConditions,
        string? insurance,
        string? pickupInstructions,
        string? deliveryNotes,
        string? customerNotes,
        IReadOnlySet<PublicProfileSection> hiddenSections)
    {
        RentalConditions = rentalConditions;
        Insurance = insurance;
        PickupInstructions = pickupInstructions;
        DeliveryNotes = deliveryNotes;
        CustomerNotes = customerNotes;
        HiddenSections = hiddenSections;
    }

    // For EF, which sets every property from its column afterwards.
    private PublicProfile() : this(null, null, null, null, null, new HashSet<PublicProfileSection>())
    {
    }

    public string? RentalConditions { get; }
    public string? Insurance { get; }
    public string? PickupInstructions { get; }
    public string? DeliveryNotes { get; }
    public string? CustomerNotes { get; }

    /// <summary>The sections the office has hidden from customers. Nothing is hidden unless chosen.</summary>
    public IReadOnlySet<PublicProfileSection> HiddenSections { get; }

    /// <summary>
    /// Nothing written and nothing hidden: where every office starts, and what every existing office
    /// has after the migration.
    /// </summary>
    /// <remarks>
    /// A new instance each call, never a shared one. EF tracks an owned value by reference, and one
    /// instance handed to two dealers saves null for the second or severs the first.
    /// </remarks>
    public static PublicProfile Empty() =>
        new(null, null, null, null, null, new HashSet<PublicProfileSection>());

    /// <summary>The office's page as submitted, every text held to <see cref="ProfileText"/>.</summary>
    /// <param name="hiddenSectionNames">
    /// The sections to hide, by name. A name that is not a section is refused rather than ignored: an
    /// owner who ticked "hide" must not be told it saved when nothing was hidden.
    /// </param>
    public static Result<PublicProfile, Error> Create(
        string? rentalConditions,
        string? insurance,
        string? pickupInstructions,
        string? deliveryNotes,
        string? customerNotes,
        IEnumerable<string>? hiddenSectionNames)
    {
        var conditions = ProfileText.Normalize(rentalConditions, PublicProfileSection.RentalConditions);
        if (conditions.IsFailure)
            return conditions.Error;

        var cover = ProfileText.Normalize(insurance, PublicProfileSection.Insurance);
        if (cover.IsFailure)
            return cover.Error;

        var pickup = ProfileText.Normalize(pickupInstructions, PublicProfileSection.PickupInstructions);
        if (pickup.IsFailure)
            return pickup.Error;

        var delivery = ProfileText.Normalize(deliveryNotes, PublicProfileSection.DeliveryNotes);
        if (delivery.IsFailure)
            return delivery.Error;

        var notes = ProfileText.Normalize(customerNotes, PublicProfileSection.CustomerNotes);
        if (notes.IsFailure)
            return notes.Error;

        var hidden = new HashSet<PublicProfileSection>();
        foreach (var name in hiddenSectionNames ?? [])
        {
            var section = PublicProfileSection.FromNameOrNull(name);
            if (section is null)
                return DealerErrors.UnknownProfileSection;
            hidden.Add(section);
        }

        return new PublicProfile(
            conditions.Value, cover.Value, pickup.Value, delivery.Value, notes.Value, hidden);
    }

    public bool IsHidden(PublicProfileSection section) => HiddenSections.Contains(section);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return RentalConditions;
        yield return Insurance;
        yield return PickupInstructions;
        yield return DeliveryNotes;
        yield return CustomerNotes;
        foreach (var section in HiddenSections.OrderBy(section => section.Id))
            yield return section;
    }
}

/// <summary>
/// What a customer is shown of an office's own words: each section's text, or null.
/// </summary>
/// <remarks>
/// Null means only "nothing to show" — hidden, never written, or delivery notes from an office that does
/// not deliver — and deliberately does not say which. Built by <see cref="Dealer.VisiblePublicProfile"/>
/// and nowhere else.
/// </remarks>
public sealed record PublicProfileView(
    string? About,
    string? RentalConditions,
    string? Insurance,
    string? PickupInstructions,
    string? DeliveryNotes,
    string? CustomerNotes);
