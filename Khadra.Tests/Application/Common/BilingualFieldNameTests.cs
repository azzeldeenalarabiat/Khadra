using CSharpFunctionalExtensions;
using Khadra.Application.Common.Behaviors;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Dealers.CustomerPage;
using Khadra.Application.Dealers.Dtos;
using Khadra.Application.Fleet.Dtos;
using Khadra.Application.Fleet.ManageVehicles;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Fleet;

namespace Khadra.Tests.Application.Common;

/// <summary>
/// The NAME a refusal about one language of one field arrives under.
/// </summary>
/// <remarks>
/// <para>
/// A console showing a per-language message needs to know which box a refusal belongs to, and it has
/// exactly one thing to go on: the key in `errors`. That key is not chosen anywhere on purpose — it
/// falls out of FluentValidation's property path and <c>ValidationBehavior</c>'s camel-casing, which
/// lowers the FIRST letter only. So `About.Ar` arrives as `about.Ar`, and a console asking for
/// `aboutAr` finds nothing and silently shows a section with no error on it.
/// </para>
/// <para>
/// That is not a hypothetical: it is what this console did until these names were pinned down. The
/// keys are asserted here, in full, so the console's lookup and the server's naming cannot drift
/// apart without a red test — a change of validator shape moves the name and nothing else would
/// notice.
/// </para>
/// </remarks>
public sealed class BilingualFieldNameTests
{
    /// <summary>Longer than any of these fields allows, so both boxes are refused at once.</summary>
    private static string TooLong(int max) => new('x', max + 1);

    private static async Task<IReadOnlyDictionary<string, string[]>> KeysFor(
        UpdateDealerCustomerPageCommand command)
    {
        var behavior = new ValidationBehavior<UpdateDealerCustomerPageCommand, Result<DealerCustomerPageDto, Error>>(
            [new UpdateDealerCustomerPageCommandValidator()]);

        var result = await behavior.Handle(
            command,
            _ => throw new InvalidOperationException("The handler must not be reached."),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error.Details);
        return result.Error.Details!;
    }

    [Fact]
    public async Task A_customer_page_refusal_names_the_section_and_the_language()
    {
        var over = TooLong(ProfileText.MaxLength);
        var details = await KeysFor(new UpdateDealerCustomerPageCommand(
            Id.New(),
            About: new LocalizedInput(over, over),
            RentalConditions: LocalizedInput.Nothing,
            Insurance: new LocalizedInput(null, over),
            PickupInstructions: new LocalizedInput(over, null),
            DeliveryNotes: LocalizedInput.Nothing,
            CustomerNotes: LocalizedInput.Nothing,
            HiddenSections: []));

        // The dotted path of the body the console sent — `{ "about": { "ar": …, "en": … } }` — with
        // only the leading letter lowered. The console looks these up case-insensitively, which is
        // the one part of the name it is allowed not to care about.
        Assert.Equal(
            ["about.Ar", "about.En", "insurance.En", "pickupInstructions.Ar"],
            details.Keys.OrderBy(key => key, StringComparer.Ordinal));

        // And a refused box says something about itself, rather than about the form.
        Assert.All(details.Values, messages => Assert.NotEmpty(messages));
    }

    [Fact]
    public async Task Only_the_language_that_was_refused_is_named()
    {
        // The whole point of a per-language name: the other box is fine and must not be marked.
        var details = await KeysFor(new UpdateDealerCustomerPageCommand(
            Id.New(),
            About: new LocalizedInput("مكتب عائلي.", TooLong(ProfileText.MaxLength)),
            RentalConditions: LocalizedInput.Nothing,
            Insurance: LocalizedInput.Nothing,
            PickupInstructions: LocalizedInput.Nothing,
            DeliveryNotes: LocalizedInput.Nothing,
            CustomerNotes: LocalizedInput.Nothing,
            HiddenSections: []));

        Assert.Equal(["about.En"], details.Keys);
    }

    [Fact]
    public async Task A_vehicle_refusal_names_the_description_and_the_language()
    {
        var over = TooLong(VehicleDetails.MaxDescriptionLength);
        var behavior = new ValidationBehavior<UpdateVehicleCommand, Result<VehicleDto, Error>>(
            [new UpdateVehicleCommandValidator()]);

        var result = await behavior.Handle(
            new UpdateVehicleCommand(Id.New(), Id.New(), new VehicleDetailsInput(
                CarTypeId: Guid.NewGuid(),
                Make: "Toyota",
                Model: "Corolla",
                Year: DateTime.UtcNow.Year,
                Color: null,
                Seats: 5,
                Transmission: "Automatic",
                FuelType: "Petrol",
                Description: new LocalizedTextDto(over, over),
                PlateNumber: "12-34567",
                DailyRate: 30m,
                SecurityDeposit: 100m,
                IsDeliveryEligible: false,
                Mileage: new MileagePolicyInput(true, null, null),
                FuelPolicy: "SameToSame")),
            _ => throw new InvalidOperationException("The handler must not be reached."),
            CancellationToken.None);

        Assert.True(result.IsFailure);

        // Note the `details.` prefix: the car's fields travel inside `Details` on the command, and
        // `SetValidator` prepends the parent's name. It is a different path from the customer page's
        // and the fleet form has to ask for this one — which is exactly why both are written down.
        Assert.Equal(
            ["details.Description.Ar", "details.Description.En"],
            result.Error.Details!.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }
}
