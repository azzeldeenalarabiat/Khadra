using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Dealers;

/// <summary>
/// What a rental office writes for its customers, in Arabic and in English.
/// </summary>
/// <remarks>
/// <para>
/// Two rules carry this whole feature, and both are here.
/// </para>
/// <para>
/// **The fallback shows the office's own words rather than an empty heading.** An office writes what
/// it has time to write; a customer reading the other language is shown what there is, tagged with
/// what it actually is. Nothing is translated and nothing is copied between the languages — see
/// <see cref="LocalizedText.Resolve"/>, which is the single place this is decided, on the server,
/// because two clients that cannot see each other would drift.
/// </para>
/// <para>
/// **Hiding beats the fallback.** A hidden section stays hidden from both audiences. Resolving first
/// would have made "is there anything to show" depend on who is reading, which is the one thing the
/// silence around a hidden section is protecting: a customer cannot tell a hidden section from one
/// never written, and that is deliberate.
/// </para>
/// </remarks>
public sealed class PublicProfileTests
{
    private const string ArabicConditions = "ممنوع التدخين داخل السيارة.";
    private const string EnglishConditions = "No smoking in the vehicle.";

    private static PublicProfile Profile(
        LocalizedInput? rentalConditions = null,
        LocalizedInput? insurance = null,
        LocalizedInput? pickupInstructions = null,
        LocalizedInput? deliveryNotes = null,
        LocalizedInput? customerNotes = null,
        params string[] hidden)
    {
        var profile = PublicProfile.Create(
            rentalConditions ?? LocalizedInput.Nothing,
            insurance ?? LocalizedInput.Nothing,
            pickupInstructions ?? LocalizedInput.Nothing,
            deliveryNotes ?? LocalizedInput.Nothing,
            customerNotes ?? LocalizedInput.Nothing,
            hidden);
        Assert.True(profile.IsSuccess, profile.IsFailure ? profile.Error.Code : null);
        return profile.Value;
    }

    // ── One text, four states ──────────────────────────────────────────────────────────────────
    //
    // Every combination the owner asked to see tested, from both sides, asserting the TEXT and the
    // language tag that comes with it. The tag matters as much as the words: it is what lets a
    // screen say "shown in English" honestly, and it is the office's claim about what it typed
    // rather than a measurement of the characters.

    [Fact]
    public void Only_Arabic_written_is_shown_to_both_readers_as_Arabic()
    {
        var text = LocalizedText.From(ArabicConditions, null);

        Assert.Equal(new ResolvedText(ArabicConditions, Language.Arabic), text.Resolve(Language.Arabic));
        Assert.Equal(new ResolvedText(ArabicConditions, Language.Arabic), text.Resolve(Language.English));
    }

    [Fact]
    public void Only_English_written_is_shown_to_both_readers_as_English()
    {
        var text = LocalizedText.From(null, EnglishConditions);

        Assert.Equal(new ResolvedText(EnglishConditions, Language.English), text.Resolve(Language.English));
        Assert.Equal(new ResolvedText(EnglishConditions, Language.English), text.Resolve(Language.Arabic));
    }

    [Fact]
    public void Both_written_gives_each_reader_their_own()
    {
        var text = LocalizedText.From(ArabicConditions, EnglishConditions);

        Assert.Equal(new ResolvedText(ArabicConditions, Language.Arabic), text.Resolve(Language.Arabic));
        Assert.Equal(new ResolvedText(EnglishConditions, Language.English), text.Resolve(Language.English));
    }

    [Fact]
    public void Neither_written_is_nothing_to_show_in_either_language()
    {
        var text = LocalizedText.From(null, null);

        Assert.True(text.IsEmpty);
        Assert.Null(text.Resolve(Language.Arabic));
        Assert.Null(text.Resolve(Language.English));
    }

    [Fact]
    public void Blank_in_a_box_is_not_written_rather_than_an_empty_string()
    {
        // A cleared box and an untouched one are the same thing to every reader, so a page is never
        // "written" with nothing on it.
        var text = LocalizedText.From("   ", "\n\t ");

        Assert.True(text.IsEmpty);
        Assert.Null(text.Resolve(Language.Arabic));
    }

    // ── Hiding, which is decided before any of that ────────────────────────────────────────────

    [Fact]
    public void A_hidden_section_stays_hidden_from_both_readers_however_much_is_written()
    {
        var dealer = Build.ApprovedDealer();
        Assert.True(dealer.UpdatePublicProfile(
            LocalizedInput.Nothing,
            Profile(
                rentalConditions: Build.Both(ArabicConditions, EnglishConditions),
                hidden: ["RentalConditions"])).IsSuccess);

        Assert.Null(dealer.VisiblePublicProfile(Language.Arabic).RentalConditions);
        Assert.Null(dealer.VisiblePublicProfile(Language.English).RentalConditions);
    }

    [Fact]
    public void A_section_written_in_one_language_only_is_still_shown_to_the_other_reader()
    {
        var dealer = Build.ApprovedDealer();
        Assert.True(dealer.UpdatePublicProfile(
            LocalizedInput.Nothing,
            Profile(rentalConditions: Build.Ar(ArabicConditions))).IsSuccess);

        var english = dealer.VisiblePublicProfile(Language.English).RentalConditions;

        Assert.NotNull(english);
        Assert.Equal(ArabicConditions, english!.Value.Text);
        Assert.Equal(Language.Arabic, english.Value.Language);
    }

    [Fact]
    public void The_About_text_follows_the_same_rules_as_every_other_section()
    {
        // It lives on the Dealer rather than on the profile, and predates both — so it is the one
        // section that could have been left behind by this change.
        var dealer = Build.ApprovedDealer();
        Assert.True(dealer.UpdatePublicProfile(Build.Ar("مكتب عائلي منذ 2004."), Profile()).IsSuccess);

        var shown = dealer.VisiblePublicProfile(Language.English).About;

        Assert.NotNull(shown);
        Assert.Equal(Language.Arabic, shown!.Value.Language);
    }

    [Fact]
    public void Delivery_notes_describe_a_service_an_office_that_does_not_deliver_is_not_offering()
    {
        var dealer = Build.ApprovedDealer();
        Assert.True(dealer.UpdatePublicProfile(
            LocalizedInput.Nothing,
            Profile(deliveryNotes: Build.Both("نوصل إلى المطار.", "We deliver to the airport."))).IsSuccess);

        // Delivery is off by default, so this is true of a new office. Not hidden, not unwritten —
        // simply not applicable, and a customer is told none of the three apart.
        Assert.Null(dealer.VisiblePublicProfile(Language.Arabic).DeliveryNotes);
        Assert.Null(dealer.VisiblePublicProfile(Language.English).DeliveryNotes);
    }

    // ── The text itself, per language ──────────────────────────────────────────────────────────

    [Fact]
    public void Line_endings_are_settled_and_the_ends_are_trimmed_in_each_language()
    {
        var profile = Profile(rentalConditions: Build.Both(
            "  ممنوع التدخين.\r\nالسيارة بخزان ممتلئ.  ",
            "  No smoking.\r\nFull tank.\rChild seat on request.  "));

        Assert.Equal("ممنوع التدخين.\nالسيارة بخزان ممتلئ.", profile.RentalConditions.Ar);
        Assert.Equal("No smoking.\nFull tank.\nChild seat on request.", profile.RentalConditions.En);
    }

    [Fact]
    public void Each_language_gets_the_whole_allowance_to_itself()
    {
        // 2000 characters of Arabic must not eat into what the same office may write in English
        // about the same thing.
        var longest = new string('x', ProfileText.MaxLength);
        var profile = Profile(customerNotes: Build.Both(longest, longest));

        Assert.Equal(longest, profile.CustomerNotes.Ar);
        Assert.Equal(longest, profile.CustomerNotes.En);
    }

    [Fact]
    public void One_character_too_many_is_refused_not_cut_and_says_which_box()
    {
        // Refused rather than cut: a rental condition truncated mid-sentence can say the opposite of
        // what the office wrote. And it names the LANGUAGE, because with two boxes under one heading
        // "rental conditions is too long" leaves an owner to guess which of the two to shorten.
        var refused = PublicProfile.Create(
            Build.Ar(new string('ب', ProfileText.MaxLength + 1)),
            LocalizedInput.Nothing, LocalizedInput.Nothing, LocalizedInput.Nothing, LocalizedInput.Nothing, []);

        Assert.True(refused.IsFailure);
        Assert.Equal("dealer.profile_text_too_long", refused.Error.Code);
        Assert.True(refused.Error.Details!.ContainsKey("rentalConditionsAr"));
        Assert.False(refused.Error.Details!.ContainsKey("rentalConditionsEn"));
    }

    [Fact]
    public void A_control_character_no_screen_can_show_is_refused_in_either_language()
    {
        var arabic = PublicProfile.Create(Build.Ar("شروط\0هنا"), default, default, default, default, []);
        var english = PublicProfile.Create(Build.En("Terms\0here"), default, default, default, default, []);

        Assert.True(arabic.IsFailure);
        Assert.Equal("dealer.invalid_profile_text", arabic.Error.Code);
        Assert.True(arabic.Error.Details!.ContainsKey("rentalConditionsAr"));

        Assert.True(english.IsFailure);
        Assert.True(english.Error.Details!.ContainsKey("rentalConditionsEn"));
    }

    [Fact]
    public void Arabic_keeps_everything_it_needs()
    {
        // Diacritics, the tatweel, an Arabic-Indic digit and the comma. All ordinary characters that
        // a control-character filter written carelessly would strip.
        const string arabic = "الشروط: ٥٠ دينارًا، التأمين مشمولـ";
        var profile = Profile(rentalConditions: Build.Ar(arabic));

        Assert.Equal(arabic, profile.RentalConditions.Ar);
    }

    // ── The page as a whole ────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_office_starts_with_a_page_that_is_empty_rather_than_absent()
    {
        var empty = PublicProfile.Empty();

        Assert.True(empty.RentalConditions.IsEmpty);
        Assert.True(empty.Insurance.IsEmpty);
        Assert.True(empty.PickupInstructions.IsEmpty);
        Assert.True(empty.DeliveryNotes.IsEmpty);
        Assert.True(empty.CustomerNotes.IsEmpty);
        Assert.Empty(empty.HiddenSections);
    }

    [Fact]
    public void A_section_name_nobody_has_is_refused_rather_than_ignored()
    {
        // An owner who ticked "hide" must not be told it saved when nothing was hidden.
        var refused = PublicProfile.Create(LocalizedInput.Nothing, LocalizedInput.Nothing, LocalizedInput.Nothing, LocalizedInput.Nothing, LocalizedInput.Nothing, ["About", "Prices"]);

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
    public void A_refused_About_leaves_the_page_exactly_as_it_was()
    {
        var dealer = Build.ApprovedDealer();
        Assert.True(dealer.UpdatePublicProfile(
            Build.En("Family run since 2004."),
            Profile(rentalConditions: Build.En(EnglishConditions))).IsSuccess);

        // One bad box must not half-save the page: the profile argument is perfectly good here, and
        // the About text is what fails.
        var refused = dealer.UpdatePublicProfile(
            Build.En("Now with\0a control character."),
            Profile(rentalConditions: Build.En("Something else entirely.")));

        Assert.True(refused.IsFailure);
        Assert.Equal("Family run since 2004.", dealer.Description.En);
        Assert.Equal(EnglishConditions, dealer.PublicProfile.RentalConditions.En);
    }

    [Fact]
    public void A_section_can_be_asked_for_by_name()
    {
        // The console walks the sections rather than naming each one, so that a section the platform
        // adds appears in the editor without a console release.
        var profile = Profile(insurance: Build.Both("التأمين مشمول.", "Insurance included."));

        Assert.Equal("التأمين مشمول.", profile.TextFor(PublicProfileSection.Insurance).Ar);
        Assert.Equal("Insurance included.", profile.TextFor(PublicProfileSection.Insurance).En);
    }
}
