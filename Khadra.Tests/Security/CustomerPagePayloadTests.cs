using System.Text.Json;
using System.Text.Json.Nodes;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Tests.Support;
using Khadra.WebAPI.Controllers;

namespace Khadra.Tests.Security;

/// <summary>
/// The body the console sends for the customer page, at the wire.
/// </summary>
/// <remarks>
/// <para>
/// **This exists to stop a deployment wiping a live page.** `UpdateDealerCustomerPageCommand` is a
/// FULL REPLACEMENT — a section left out is a section cleared, deliberately, so that text an owner
/// can no longer see cannot stay on a page customers can. And `System.Text.Json` ignores unknown
/// members by default.
/// </para>
/// <para>
/// Put those two together and a console tab opened before the bilingual release, saving its old body
/// of six flat strings, binds to all-null here — and the API answers 200 having cleared every
/// section of a page that was fine a second earlier. Not a bug in anybody's code: a tab left open
/// across a deploy. That is the overwrite the owner forbade, arriving as an accident.
/// </para>
/// <para>
/// `[JsonUnmappedMemberHandling(Disallow)]` on the request makes that body a 400 instead. These
/// tests deserialise with the options MVC actually uses, because the whole failure lived in what the
/// serialiser does with a field it does not recognise.
/// </para>
/// </remarks>
public sealed class CustomerPagePayloadTests
{
    /// <summary>What MVC reads a request body with here: nothing in the API customises it.</summary>
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The body a console built before this change sent: one flat string per section.</summary>
    private static JsonObject OldConsoleBody() => new()
    {
        ["about"] = "Family-run since 2014.",
        ["rentalConditions"] = "No smoking.",
        ["insurance"] = "Comprehensive, 200 JOD excess.",
        ["pickupInstructions"] = "Bring the original licence.",
        ["deliveryNotes"] = "We deliver to the airport.",
        ["customerNotes"] = "Ask for Rami.",
        ["hiddenSections"] = new JsonArray("Insurance"),
    };

    /// <summary>The body this console sends: a pair per section.</summary>
    private static JsonObject NewConsoleBody() => new()
    {
        ["about"] = new JsonObject { ["ar"] = "مكتب عائلي منذ 2014.", ["en"] = "Family-run since 2014." },
        ["rentalConditions"] = new JsonObject { ["ar"] = "ممنوع التدخين.", ["en"] = "No smoking." },
        ["insurance"] = new JsonObject { ["ar"] = null, ["en"] = "Comprehensive, 200 JOD excess." },
        ["pickupInstructions"] = new JsonObject { ["ar"] = "أحضر الرخصة الأصلية.", ["en"] = null },
        ["deliveryNotes"] = new JsonObject { ["ar"] = null, ["en"] = null },
        ["customerNotes"] = new JsonObject { ["ar"] = null, ["en"] = null },
        ["hiddenSections"] = new JsonArray("Insurance"),
    };

    private static CustomerPageRequestReadResult Read(JsonObject body)
    {
        try
        {
            var request = JsonSerializer.Deserialize<DealersController.CustomerPageRequest>(
                body.ToJsonString(), Web);
            return new CustomerPageRequestReadResult(request, null);
        }
        catch (JsonException failure)
        {
            // What MVC turns into a 400 ProblemDetails: an unreadable body never reaches an action.
            return new CustomerPageRequestReadResult(null, failure);
        }
    }

    private sealed record CustomerPageRequestReadResult(
        DealersController.CustomerPageRequest? Request,
        JsonException? Refused);

    [Fact]
    public void An_old_console_body_is_refused_rather_than_read_as_an_empty_page()
    {
        var result = Read(OldConsoleBody());

        Assert.Null(result.Request);
        Assert.NotNull(result.Refused);
    }

    [Fact]
    public void Every_old_field_name_is_refused_on_its_own()
    {
        // One at a time, because a body carrying ANY of them is an old console — and because a
        // future rename that accidentally re-adds one of these names would otherwise pass.
        foreach (var name in new[]
                 {
                     "about", "rentalConditions", "insurance",
                     "pickupInstructions", "deliveryNotes", "customerNotes",
                 })
        {
            var body = NewConsoleBody();
            body[name] = "a flat string, the way the old console wrote it";

            Assert.NotNull(Read(body).Refused);
        }
    }

    [Fact]
    public void A_refused_body_cannot_have_changed_anything()
    {
        // The proof that a refusal is safe rather than merely correct: the page is only ever written
        // by the handler, the handler is only ever reached through a body that BOUND, and an old
        // body does not bind. So there is no path from that request to a write.
        //
        // Held here as a live assertion rather than as a paragraph: a dealer's saved page, a refused
        // request, and the page still exactly as it was.
        var dealer = Build.ApprovedDealer();
        Assert.True(dealer.UpdatePublicProfile(
            Build.Both("مكتب عائلي منذ 2014.", "Family-run since 2014."),
            PublicProfile.Create(
                Build.Both("ممنوع التدخين.", "No smoking."),
                Build.En("Comprehensive, 200 JOD excess."),
                Build.Ar("أحضر الرخصة الأصلية."),
                LocalizedInput.Nothing,
                LocalizedInput.Nothing,
                ["Insurance"]).Value).IsSuccess);

        var before = dealer.PublicProfile;
        var beforeAbout = dealer.Description;

        Assert.NotNull(Read(OldConsoleBody()).Refused);

        // Every language of every section, untouched.
        Assert.Equal(beforeAbout, dealer.Description);
        Assert.Equal("مكتب عائلي منذ 2014.", dealer.Description.Ar);
        Assert.Equal("Family-run since 2014.", dealer.Description.En);
        Assert.Equal(before, dealer.PublicProfile);
        Assert.Equal("ممنوع التدخين.", dealer.PublicProfile.RentalConditions.Ar);
        Assert.Equal("No smoking.", dealer.PublicProfile.RentalConditions.En);
        Assert.Null(dealer.PublicProfile.Insurance.Ar);
        Assert.Equal("Comprehensive, 200 JOD excess.", dealer.PublicProfile.Insurance.En);
        Assert.Equal("أحضر الرخصة الأصلية.", dealer.PublicProfile.PickupInstructions.Ar);
        Assert.Null(dealer.PublicProfile.PickupInstructions.En);
        Assert.True(dealer.PublicProfile.IsHidden(PublicProfileSection.Insurance));
    }

    [Fact]
    public void The_body_this_console_sends_is_read_whole()
    {
        // The other half: `Disallow` must refuse the old shape without refusing the new one.
        var request = Read(NewConsoleBody()).Request;

        Assert.NotNull(request);
        Assert.Equal("مكتب عائلي منذ 2014.", request!.About!.Ar);
        Assert.Equal("Family-run since 2014.", request.About.En);
        Assert.Equal("ممنوع التدخين.", request.RentalConditions!.Ar);
        Assert.Null(request.Insurance!.Ar);
        Assert.Equal("Comprehensive, 200 JOD excess.", request.Insurance.En);
        Assert.Equal("أحضر الرخصة الأصلية.", request.PickupInstructions!.Ar);
        Assert.Null(request.PickupInstructions.En);
        Assert.Equal(["Insurance"], request.HiddenSections);
    }

    [Fact]
    public void A_section_left_out_entirely_is_still_a_section_cleared()
    {
        // `Disallow` refuses fields it does not KNOW; it does not require every field to be present.
        // The full-replacement rule is unchanged, and the console is what must send all six.
        var body = NewConsoleBody();
        body.Remove("rentalConditions");

        var request = Read(body).Request;

        Assert.NotNull(request);
        Assert.Null(request!.RentalConditions);
    }
}
