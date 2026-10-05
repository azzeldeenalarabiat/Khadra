using Khadra.Domain.Common;
using Khadra.Domain.Legal;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Legal;

/// <summary>
/// A published version of a legal text (Wave 2 G1): what it accepts, what it stores, and the order versions come in.
/// </summary>
public sealed class LegalDocumentVersionTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly Id AdminId = Id.New();

    private static CSharpFunctionalExtensions.Result<LegalDocumentVersion, Error> Publish(
        string? label = "2026-10",
        string? english = "Terms.",
        string? arabic = "الشروط.",
        DateTimeOffset? latest = null) =>
        LegalDocumentVersion.Publish(LegalDocumentKind.Terms, label, english, arabic, AdminId, Now, latest);

    [Fact]
    public void A_version_is_in_force_the_moment_it_is_published()
    {
        var version = Publish(label: "  2026-10  ").Value;

        Assert.Same(LegalDocumentKind.Terms, version.Kind);
        Assert.Equal("2026-10", version.VersionLabel);
        Assert.Equal(Now, version.PublishedAt);
        Assert.Equal(version.PublishedAt, version.EffectiveFrom);
        Assert.Equal(AdminId, version.PublishedByAdminId);
        Assert.False(version.Id.IsEmpty);
    }

    /// <summary>The hash is what <c>sha256sum</c> prints for the same bytes, so an approved file can be checked against it.</summary>
    [Fact]
    public void Each_text_is_hashed_on_its_own_as_sha256sum_would_hash_the_same_file()
    {
        var version = Publish(english: "abc").Value;

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", version.BodyEnSha256);
        Assert.Equal(LegalDocumentVersion.Sha256Hex("الشروط."), version.BodyArSha256);
        Assert.Equal(LegalDocumentVersion.HashLength, version.BodyArSha256.Length);
    }

    [Fact]
    public void Line_ends_are_stored_as_LF_whatever_they_were_typed_with()
    {
        var version = Publish(english: "One.\r\nTwo.\rThree.\n").Value;

        Assert.Equal("One.\nTwo.\nThree.\n", version.BodyEn);
        Assert.Equal(LegalDocumentVersion.Sha256Hex("One.\nTwo.\nThree.\n"), version.BodyEnSha256);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("12345678901234567890123456789012345678901")]
    [InlineData("2026\t10")]
    public void A_label_is_required_short_and_free_of_control_characters(string? label) =>
        Assert.Equal("legal.label_invalid", Publish(label: label).Error.Code);

    [Fact]
    public void Both_texts_are_required()
    {
        Assert.Equal("legal.body_required", Publish(english: " \n\t ").Error.Code);
        Assert.Equal("legal.body_required", Publish(arabic: null).Error.Code);
    }

    [Fact]
    public void A_text_longer_than_the_guard_is_refused()
    {
        Assert.True(Publish(english: new string('a', LegalDocumentVersion.MaxBodyLength)).IsSuccess);
        Assert.Equal("legal.body_too_long", Publish(english: new string('a', LegalDocumentVersion.MaxBodyLength + 1)).Error.Code);
    }

    /// <summary>NUL first of all: PostgreSQL text cannot hold it, which was a 500 rather than a refusal.</summary>
    [Theory]
    [InlineData("Terms.\0")]
    [InlineData("Terms.\u0007")]
    [InlineData("Terms.\u007F")]
    [InlineData("Terms.\u0085")]
    public void Control_characters_are_refused_not_cleaned(string text) =>
        Assert.Equal("legal.body_invalid_characters", Publish(english: text).Error.Code);

    /// <summary>Built in code: an attribute stores its strings as UTF-8, which cannot carry half a character.</summary>
    [Fact]
    public void Half_a_character_is_refused_because_it_has_no_bytes_to_hash()
    {
        Assert.Equal("legal.body_invalid_characters", Publish(english: "Terms." + '\uD800').Error.Code);
        Assert.Equal("legal.body_invalid_characters", Publish(english: "Terms." + '\uDC00' + "x").Error.Code);
        Assert.Equal("legal.body_invalid_characters", Publish(english: "Terms." + '\uD800' + "x").Error.Code);
    }

    [Fact]
    public void Tabs_line_breaks_and_whole_characters_outside_the_basic_plane_are_published()
    {
        var version = Publish(english: "Terms.\n\tIndented.\n\U0001F600").Value;

        Assert.Equal("Terms.\n\tIndented.\n\U0001F600", version.BodyEn);
    }

    /// <summary>The version in force is the newest, so a new one must be later than every one before it.</summary>
    [Fact]
    public void A_version_must_be_later_than_the_newest_one_already_published()
    {
        Assert.True(Publish(latest: Now.AddTicks(-1)).IsSuccess);
        Assert.Equal("legal.publish_conflict", Publish(latest: Now).Error.Code);
        Assert.Equal("legal.publish_conflict", Publish(latest: Now.AddMinutes(1)).Error.Code);
    }

    [Theory]
    [InlineData("terms", "Terms")]
    [InlineData("Terms", "Terms")]
    [InlineData("PRIVACY", "Privacy")]
    public void A_document_is_named_by_its_slug_in_any_case(string slug, string kind) =>
        Assert.Equal(kind, LegalDocumentKind.FromSlug(slug)!.Name);

    [Theory]
    [InlineData("cookies")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_document_is_nothing(string? slug)
    {
        Assert.Null(LegalDocumentKind.FromSlug(slug));
        Assert.Null(LegalDocumentKind.FromNameOrSlug(slug));
    }

    /// <summary>An administrator's request names the kind as the console sends it, by name, or by its slug.</summary>
    [Theory]
    [InlineData("Terms", "Terms")]
    [InlineData("privacy", "Privacy")]
    [InlineData("PRIVACY", "Privacy")]
    public void An_administrator_names_a_document_by_its_name_or_its_slug(string value, string kind) =>
        Assert.Equal(kind, LegalDocumentKind.FromNameOrSlug(value)!.Name);
}
