using System.Text.Json;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Common;

namespace Khadra.Tests.Security;

/// <summary>
/// What the customer-facing catalogue actually puts on the wire.
/// </summary>
/// <remarks>
/// <para>
/// **These exist because the gallery page returned a 500 and every test was green.** The read models
/// carried the DOMAIN's <see cref="ResolvedText"/>, whose <c>Language</c> is an
/// <see cref="Enumeration"/> — and <c>Language.Other</c> was a PROPERTY. `System.Text.Json` walks
/// properties: `ar.Other` is `en`, `en.Other` is `ar`, and the writer recursed to its depth limit and
/// threw. Every unit test passed, because each asserted the object and none of them serialised it.
/// </para>
/// <para>
/// So the assertion here is serialisation itself, on the records the controller returns. It is the
/// only kind of test that could have caught it, and it is cheap.
/// </para>
/// </remarks>
public sealed class CatalogueSerializationTests
{
    /// <summary>The options MVC uses. Nothing in the API customises them.</summary>
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static GallerySections Sections() => new(
        About: new ResolvedTextDto("مكتب عائلي في العبدلي.", Language.Arabic.Name),
        RentalConditions: new ResolvedTextDto("No smoking in any car.", Language.English.Name),
        Insurance: null,
        PickupInstructions: null,
        DeliveryNotes: null,
        CustomerNotes: null);

    [Fact]
    public void A_gallery_page_can_be_written_as_json_at_all()
    {
        // The whole regression in one line: this threw, and the customer saw a 500 where the
        // office's own page should have been.
        var json = JsonSerializer.Serialize(Sections(), Web);

        Assert.Contains("\"about\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_is_its_text_and_a_language_TAG_and_nothing_else()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Sections(), Web));
        var about = document.RootElement.GetProperty("about");

        // Two members. A client reads `language` as `ar` or `en`; anything richer here would be the
        // domain's enumeration leaking, which is what recursed.
        Assert.Equal(2, about.EnumerateObject().Count());
        Assert.Equal("مكتب عائلي في العبدلي.", about.GetProperty("text").GetString());
        Assert.Equal("ar", about.GetProperty("language").GetString());
        Assert.Equal(JsonValueKind.String, about.GetProperty("language").ValueKind);
    }

    [Fact]
    public void A_section_with_nothing_to_show_is_null_and_says_no_more_than_that()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Sections(), Web));

        // Hidden, never written, and delivery-notes-with-delivery-off all arrive as this. There is
        // no flag saying a section exists but is hidden, because that flag is the answer the
        // silence is protecting.
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("insurance").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("deliveryNotes").ValueKind);
    }

    [Fact]
    public void The_language_a_fallback_came_back_in_survives_the_wire()
    {
        // An English section answered to an Arabic reader. The tag is the only thing on the wire
        // that says so, and both clients set the text's locale from it.
        var sections = new GallerySections(
            About: ResolvedTextDto.From(
                LocalizedText.From(null, "Family-run since 2014.").Resolve(Language.Arabic)),
            RentalConditions: null,
            Insurance: null,
            PickupInstructions: null,
            DeliveryNotes: null,
            CustomerNotes: null);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(sections, Web));
        var about = document.RootElement.GetProperty("about");

        Assert.Equal("Family-run since 2014.", about.GetProperty("text").GetString());
        Assert.Equal("en", about.GetProperty("language").GetString());
    }

    [Fact]
    public void A_language_is_not_walkable_into_the_other_one()
    {
        // The root cause, pinned: `Other` is a METHOD. As a property, any object holding a Language
        // was unserialisable, and the next read model to carry one would have failed the same way.
        var property = typeof(Language).GetProperty(nameof(Language.Other));

        Assert.Null(property);
        Assert.Equal(Language.English, Language.Arabic.Other());
        Assert.Equal(Language.Arabic, Language.English.Other());
    }
}
