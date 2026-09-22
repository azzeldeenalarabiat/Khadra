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
        LocalizedText rentalConditions,
        LocalizedText insurance,
        LocalizedText pickupInstructions,
        LocalizedText deliveryNotes,
        LocalizedText customerNotes,
        IReadOnlySet<PublicProfileSection> hiddenSections)
    {
        RentalConditionsAr = rentalConditions.Ar;
        RentalConditionsEn = rentalConditions.En;
        InsuranceAr = insurance.Ar;
        InsuranceEn = insurance.En;
        PickupInstructionsAr = pickupInstructions.Ar;
        PickupInstructionsEn = pickupInstructions.En;
        DeliveryNotesAr = deliveryNotes.Ar;
        DeliveryNotesEn = deliveryNotes.En;
        CustomerNotesAr = customerNotes.Ar;
        CustomerNotesEn = customerNotes.En;
        HiddenSections = hiddenSections;
    }

    // For EF, which sets every property from its column afterwards.
    private PublicProfile() : this(
        default, default, default, default, default, new HashSet<PublicProfileSection>())
    {
    }

    // ── Stored: two ordinary nullable columns per section ──────────────────────────────────────
    //
    // FLAT, and deliberately. The obvious shape is a nested owned `LocalizedText` per section, and
    // it is the trap this file's own mapping comments describe twice: an owned type whose columns
    // are all nullable cannot be told apart from an absent one, so EF materialises it as null — and
    // a section nobody has written in either language is exactly that, which is most of them on most
    // pages. The pairs are read back through the computed properties below.

    public string? RentalConditionsAr { get; }
    public string? RentalConditionsEn { get; }
    public string? InsuranceAr { get; }
    public string? InsuranceEn { get; }
    public string? PickupInstructionsAr { get; }
    public string? PickupInstructionsEn { get; }
    public string? DeliveryNotesAr { get; }
    public string? DeliveryNotesEn { get; }
    public string? CustomerNotesAr { get; }
    public string? CustomerNotesEn { get; }

    // ── The domain's view of the same thing ────────────────────────────────────────────────────
    //
    // Computed and get-only, so EF leaves them alone by convention — the same fact the mapping
    // comments rely on when they warn that anything not mapped explicitly is silently dropped, here
    // working the right way round.
    //
    // Through `LocalizedText.From`, not the constructor, so a stored BLANK reads as nothing written.
    // This code never stores one, but `BilingualDealerContent` copies legacy values verbatim, and a
    // legacy '' or '   ' would otherwise come back as written text: an empty section with a heading
    // on the customer's page, and an empty "shown in English" row in the owner's preview.

    public LocalizedText RentalConditions => LocalizedText.From(RentalConditionsAr, RentalConditionsEn);
    public LocalizedText Insurance => LocalizedText.From(InsuranceAr, InsuranceEn);
    public LocalizedText PickupInstructions => LocalizedText.From(PickupInstructionsAr, PickupInstructionsEn);
    public LocalizedText DeliveryNotes => LocalizedText.From(DeliveryNotesAr, DeliveryNotesEn);
    public LocalizedText CustomerNotes => LocalizedText.From(CustomerNotesAr, CustomerNotesEn);

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
        new(default, default, default, default, default, new HashSet<PublicProfileSection>());

    /// <summary>The office's page as submitted, every text held to <see cref="ProfileText"/>.</summary>
    /// <param name="hiddenSectionNames">
    /// The sections to hide, by name. A name that is not a section is refused rather than ignored: an
    /// owner who ticked "hide" must not be told it saved when nothing was hidden.
    /// </param>
    public static Result<PublicProfile, Error> Create(
        LocalizedInput rentalConditions,
        LocalizedInput insurance,
        LocalizedInput pickupInstructions,
        LocalizedInput deliveryNotes,
        LocalizedInput customerNotes,
        IEnumerable<string>? hiddenSectionNames)
    {
        var conditions = Localize(rentalConditions, PublicProfileSection.RentalConditions);
        if (conditions.IsFailure)
            return conditions.Error;

        var cover = Localize(insurance, PublicProfileSection.Insurance);
        if (cover.IsFailure)
            return cover.Error;

        var pickup = Localize(pickupInstructions, PublicProfileSection.PickupInstructions);
        if (pickup.IsFailure)
            return pickup.Error;

        var delivery = Localize(deliveryNotes, PublicProfileSection.DeliveryNotes);
        if (delivery.IsFailure)
            return delivery.Error;

        var notes = Localize(customerNotes, PublicProfileSection.CustomerNotes);
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

    /// <summary>
    /// Both boxes of one section, normalised, or the first refusal — naming the LANGUAGE that caused
    /// it, so the console can put the message under the box the owner actually broke.
    /// </summary>
    private static Result<LocalizedText, Error> Localize(LocalizedInput input, PublicProfileSection section)
    {
        var arabic = ProfileText.Normalize(input.Ar, section, Language.Arabic);
        if (arabic.IsFailure)
            return arabic.Error;

        var english = ProfileText.Normalize(input.En, section, Language.English);
        if (english.IsFailure)
            return english.Error;

        return LocalizedText.From(arabic.Value, english.Value);
    }

    public bool IsHidden(PublicProfileSection section) => HiddenSections.Contains(section);

    /// <summary>
    /// One section's text, both languages, by section. Used where a caller is walking the sections
    /// rather than naming one — the console's editor and its two previews.
    /// </summary>
    public LocalizedText TextFor(PublicProfileSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (section == PublicProfileSection.RentalConditions) return RentalConditions;
        if (section == PublicProfileSection.Insurance) return Insurance;
        if (section == PublicProfileSection.PickupInstructions) return PickupInstructions;
        if (section == PublicProfileSection.DeliveryNotes) return DeliveryNotes;
        if (section == PublicProfileSection.CustomerNotes) return CustomerNotes;

        // About lives on the Dealer itself and predates this type; `Dealer.VisiblePublicProfile`
        // supplies it. Asking here for a section this value does not hold is a programming error.
        throw new DomainException($"{section.Name} is not a section of the public profile.");
    }

    /// <summary>
    /// Both languages of every section, for equality and for persistence round-trip tests.
    /// </summary>
    private IEnumerable<string?> Texts()
    {
        yield return RentalConditionsAr;
        yield return RentalConditionsEn;
        yield return InsuranceAr;
        yield return InsuranceEn;
        yield return PickupInstructionsAr;
        yield return PickupInstructionsEn;
        yield return DeliveryNotesAr;
        yield return DeliveryNotesEn;
        yield return CustomerNotesAr;
        yield return CustomerNotesEn;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var text in Texts())
            yield return text;
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
/// <param name="About">
/// The office's description, which lives on <see cref="Dealer.Description"/> rather than on the
/// profile — it predates this type and has been there since registration.
/// </param>
public sealed record PublicProfileView(
    ResolvedText? About,
    ResolvedText? RentalConditions,
    ResolvedText? Insurance,
    ResolvedText? PickupInstructions,
    ResolvedText? DeliveryNotes,
    ResolvedText? CustomerNotes);
