using Khadra.Domain.Common;

namespace Khadra.Domain.Dealers;

/// <summary>
/// A part of a rental office's customer-facing page that its owner writes, and may choose to hide.
/// </summary>
/// <remarks>
/// <para>
/// These six and nothing else. What a customer must be able to see before booking — the opening hours a
/// pickup is held to, whether delivery is offered and what it costs, the address and the pin, the
/// rating, the cars — is not a member, so "hide the opening hours" cannot even be expressed. And what
/// the PLATFORM decides (cancellation, payment, deposit, the documents a renter needs) is not an
/// office's to write here at all; every booking quote already states it.
/// </para>
/// <para>
/// The NAMES are a contract. They are stored in <c>dealers.hidden_profile_sections</c> and sent on the
/// wire, so renaming one would silently show every section an office had hidden under the old name.
/// Add; never rename.
/// </para>
/// </remarks>
public sealed class PublicProfileSection : Enumeration
{
    public static readonly PublicProfileSection About = new(1, "About");
    public static readonly PublicProfileSection RentalConditions = new(2, "RentalConditions");
    public static readonly PublicProfileSection Insurance = new(3, "Insurance");
    public static readonly PublicProfileSection PickupInstructions = new(4, "PickupInstructions");
    public static readonly PublicProfileSection DeliveryNotes = new(5, "DeliveryNotes");
    public static readonly PublicProfileSection CustomerNotes = new(6, "CustomerNotes");

    private PublicProfileSection(int id, string name) : base(id, name)
    {
    }

    /// <summary>
    /// The section a name refers to, ignoring case, or null for a name that is not one.
    /// </summary>
    /// <remarks>
    /// Null rather than a throw, because a name arrives from a request or from a stored string, and
    /// neither is a place to throw: a request gets a validation error, and a stored name this build no
    /// longer knows is dropped.
    /// </remarks>
    public static PublicProfileSection? FromNameOrNull(string? name) =>
        GetAll<PublicProfileSection>().FirstOrDefault(section =>
            string.Equals(section.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>How the section is named in a request body and a response: camelCase.</summary>
    /// <remarks>
    /// The stem only. Every section now travels as TWO fields, one per language, so this is what
    /// they share rather than what either is called — see <see cref="FieldName(Language)"/>.
    /// It is still the name in `hidden_profile_sections`, which is per SECTION and not per language:
    /// an office hides a section, not a translation of one.
    /// </remarks>
    public string FieldName => char.ToLowerInvariant(Name[0]) + Name[1..];

    /// <summary>
    /// How one language's box is named on the wire: `rentalConditionsAr`, `rentalConditionsEn`.
    /// </summary>
    /// <remarks>
    /// A validation error carries this so the console can put the message under the box that caused
    /// it. With two boxes per section under one heading, naming only the section would tell an owner
    /// that "rental conditions" is wrong and leave them to work out which of the two they broke.
    /// </remarks>
    public string FieldNameFor(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);
        return FieldName + (language == Language.Arabic ? "Ar" : "En");
    }
}
