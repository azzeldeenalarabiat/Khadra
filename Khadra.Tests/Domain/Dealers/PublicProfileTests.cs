using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Infrastructure.Persistence.Configurations.Dealers;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Dealers;

/// <summary>
/// The rental office's own words: what the platform accepts, and what a customer is shown of it.
/// </summary>
/// <remarks>
/// Two rules carry the weight here. A section is refused rather than CUT, because a rental condition
/// truncated mid-sentence can say the opposite of what the office wrote. And a customer cannot tell a
/// hidden section from one never written, because both are simply absent — which is what stops the
/// page becoming a way to ask what an office is keeping back.
/// </remarks>
public sealed class PublicProfileTests
{
    private static PublicProfile Profile(
        string? rentalConditions = null,
        string? insurance = null,
        string? pickupInstructions = null,
        string? deliveryNotes = null,
        string? customerNotes = null,
        params string[] hidden)
    {
        var profile = PublicProfile.Create(
            rentalConditions, insurance, pickupInstructions, deliveryNotes, customerNotes, hidden);
        Assert.True(profile.IsSuccess, profile.IsFailure ? profile.Error.Code : null);
        return profile.Value;
    }

    [Fact]
    public void Line_endings_are_settled_and_the_ends_are_trimmed()
    {
        var profile = Profile(rentalConditions: "  No smoking.\r\nExtra driver: 5 JOD.\rChild seat on request.  ");

        Assert.Equal("No smoking.\nExtra driver: 5 JOD.\nChild seat on request.", profile.RentalConditions);
    }

    [Fact]
    public void A_section_with_nothing_in_it_is_a_section_not_written()
    {
        var profile = Profile(insurance: "   \n  ", pickupInstructions: "");

        Assert.Null(profile.Insurance);
        Assert.Null(profile.PickupInstructions);
    }

    [Fact]
    public void The_longest_allowed_text_is_accepted_and_one_character_more_is_refused_not_cut()
    {
        Assert.Equal(
            new string('x', ProfileText.MaxLength),
            Profile(customerNotes: new string('x', ProfileText.MaxLength)).CustomerNotes);

        var refused = PublicProfile.Create(null, null, null, null, new string('x', ProfileText.MaxLength + 1), []);

        Assert.True(refused.IsFailure);
        Assert.Equal("dealer.profile_text_too_long", refused.Error.Code);
        // Named, so the console can put the message under the box that caused it.
        Assert.True(refused.Error.Details!.ContainsKey("customerNotes"));
    }

    [Fact]
    public void A_control_character_no_screen_can_show_is_refused()
    {
        // U+0000 in particular: PostgreSQL refuses it outright, so storing it would 500 the save.
        var refused = PublicProfile.Create("Terms\0here", null, null, null, null, []);

        Assert.True(refused.IsFailure);
        Assert.Equal("dealer.invalid_profile_text", refused.Error.Code);
        Assert.True(refused.Error.Details!.ContainsKey("rentalConditions"));
    }

    [Fact]
    public void Arabic_keeps_everything_it_needs()
    {
        // Line breaks and tabs stay; so do the bidi marks an Arabic editor inserts, which are not
        // control characters and which trimming would not remove. An emoji counts as it does in a
        // browser's character count, so the column can never overflow.
        const string arabic = "شروط الإيجار:\n\t- سائق إضافي 5 دنانير‏\n- التدخين ممنوع 🚭";

        var profile = Profile(rentalConditions: arabic);

        Assert.Equal(arabic, profile.RentalConditions);
    }

    [Fact]
    public void A_section_name_nobody_has_is_refused_rather_than_ignored()
    {
        // An owner who ticked "hide" must not be told it saved when nothing was hidden.
        var refused = PublicProfile.Create(null, null, null, null, null, ["About", "Prices"]);

        Assert.True(refused.IsFailure);
        Assert.Equal("dealer.unknown_profile_section", refused.Error.Code);
    }

    [Fact]
    public void Section_names_are_read_however_they_are_typed_and_said_once()
    {
        var profile = Profile(hidden: ["about", "ABOUT", " Insurance "]);

        Assert.Equal(2, profile.HiddenSections.Count);
        Assert.True(profile.IsHidden(PublicProfileSection.About));
        Assert.True(profile.IsHidden(PublicProfileSection.Insurance));
    }

    [Fact]
    public void An_office_starts_with_a_page_that_is_empty_rather_than_absent()
    {
        var dealer = Build.Dealer();

        Assert.NotNull(dealer.PublicProfile);
        Assert.Empty(dealer.PublicProfile.HiddenSections);
        Assert.Null(dealer.PublicProfile.RentalConditions);
    }

    [Fact]
    public void Every_section_the_office_wrote_and_did_not_hide_is_shown()
    {
        var dealer = Build.ApprovedDealer();
        Assert.True(dealer.EnableDelivery(10m, Money.Jod(5m), Build.Now).IsSuccess);
        Assert.True(dealer.UpdatePublicProfile(
            "Family-run since 2014.",
            Profile(
                rentalConditions: "No smoking.",
                insurance: "Comprehensive, 200 JOD excess.",
                pickupInstructions: "Bring the original licence.",
                deliveryNotes: "We deliver to the airport.",
                customerNotes: "Ask for Rami.")).IsSuccess);

        var shown = dealer.VisiblePublicProfile();

        Assert.Equal("Family-run since 2014.", shown.About);
        Assert.Equal("No smoking.", shown.RentalConditions);
        Assert.Equal("Comprehensive, 200 JOD excess.", shown.Insurance);
        Assert.Equal("Bring the original licence.", shown.PickupInstructions);
        Assert.Equal("We deliver to the airport.", shown.DeliveryNotes);
        Assert.Equal("Ask for Rami.", shown.CustomerNotes);
    }

    [Fact]
    public void A_hidden_section_and_one_never_written_are_both_simply_absent()
    {
        var dealer = Build.ApprovedDealer();
        Assert.True(dealer.UpdatePublicProfile(
            "Family-run since 2014.",
            Profile(
                rentalConditions: "No smoking.",
                hidden: ["About", "RentalConditions"])).IsSuccess);

        var shown = dealer.VisiblePublicProfile();

        // Hidden although written...
        Assert.Null(shown.About);
        Assert.Null(shown.RentalConditions);
        // ...and never written at all. A customer cannot tell which is which.
        Assert.Null(shown.Insurance);
        // The text itself is still there, for the office to show again.
        Assert.Equal("Family-run since 2014.", dealer.Description);
        Assert.Equal("No smoking.", dealer.PublicProfile.RentalConditions);
    }

    [Fact]
    public void Delivery_notes_describe_a_service_an_office_that_does_not_deliver_is_not_offering()
    {
        var dealer = Build.ApprovedDealer();
        Assert.True(dealer.UpdatePublicProfile(
            null, Profile(deliveryNotes: "We deliver to the airport.")).IsSuccess);

        Assert.Null(dealer.VisiblePublicProfile().DeliveryNotes);

        Assert.True(dealer.EnableDelivery(10m, Money.Jod(5m), Build.Now).IsSuccess);
        Assert.Equal("We deliver to the airport.", dealer.VisiblePublicProfile().DeliveryNotes);

        dealer.DisableDelivery(Build.Now);
        Assert.Null(dealer.VisiblePublicProfile().DeliveryNotes);
    }

    [Fact]
    public void A_refused_About_leaves_the_page_exactly_as_it_was()
    {
        var dealer = Build.ApprovedDealer();
        Assert.True(dealer.UpdatePublicProfile("Family-run since 2014.", Profile(insurance: "Comprehensive.")).IsSuccess);

        var refused = dealer.UpdatePublicProfile(new string('x', ProfileText.MaxLength + 1), Profile());

        Assert.True(refused.IsFailure);
        Assert.Equal("dealer.profile_text_too_long", refused.Error.Code);
        Assert.Equal("Family-run since 2014.", dealer.Description);
        Assert.Equal("Comprehensive.", dealer.PublicProfile.Insurance);
    }

    [Fact]
    public void A_save_is_the_whole_page_so_a_section_left_out_is_a_section_cleared()
    {
        // Never one quietly kept from an earlier save the owner can no longer see.
        var dealer = Build.ApprovedDealer();
        Assert.True(dealer.UpdatePublicProfile("About us.", Profile(insurance: "Comprehensive.")).IsSuccess);

        Assert.True(dealer.UpdatePublicProfile(null, Profile(rentalConditions: "No smoking.")).IsSuccess);

        Assert.Null(dealer.Description);
        Assert.Null(dealer.PublicProfile.Insurance);
        Assert.Equal("No smoking.", dealer.PublicProfile.RentalConditions);
    }

    [Fact]
    public void An_applicant_prepares_the_page_before_anybody_has_approved_the_office()
    {
        // Nothing here reaches a customer until the office may trade, and an applicant sent back for
        // clarification is exactly who is writing it.
        var applicant = Build.Dealer();

        Assert.True(applicant.UpdatePublicProfile("About us.", Profile(insurance: "Comprehensive.")).IsSuccess);
    }

    [Fact]
    public void The_six_section_names_are_the_ones_stored_and_sent()
    {
        // WRITTEN OUT, on purpose. These names are persisted in `dealers.hidden_profile_sections` and
        // travel on the wire, so renaming one silently shows every section an office had hidden under
        // the old name. The test that compares the API's list against `GetAll` cannot catch that: it
        // asks the same source twice and agrees with itself through any rename.
        //
        // Add a name below when the platform adds a section. Never change one.
        Assert.Equal(
            ["About", "RentalConditions", "Insurance", "PickupInstructions", "DeliveryNotes", "CustomerNotes"],
            Enumeration.GetAll<PublicProfileSection>().Select(section => section.Name));
    }

    [Fact]
    public void Every_section_at_once_still_fits_the_column_that_stores_them()
    {
        // "Add; never rename" means this string only ever grows, and the column is varchar(200). The
        // day it is crossed, the failure lands on the dealers who hid EVERYTHING — a Postgres 22001 on
        // save — and no test would see it first: the persistence suite runs on SQLite, which has no
        // length limit to violate.
        var everything = string.Join(
            ';',
            Enumeration.GetAll<PublicProfileSection>().Select(section => section.Name));

        Assert.True(
            everything.Length <= HiddenSectionsConverter.MaxLength,
            $"every section hidden is {everything.Length} characters, and the column holds " +
            $"{HiddenSectionsConverter.MaxLength}. Widen the column in a new migration before adding " +
            "another section.");
    }
}
