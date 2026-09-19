using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Dealers.Dtos;
using Khadra.Application.Dealers.UpdateProfile;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Tests.Support;
using Khadra.WebAPI.Controllers;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Khadra.Tests.Security;

/// <summary>
/// Saving the dealer page, at the wire: the body the console sends and the command it becomes.
/// </summary>
/// <remarks>
/// This is the seam that erased every office's city and address. The request had no location, the
/// controller built the command with five arguments, the command's defaults filled in nulls, and the
/// aggregate stored them. Every test below the controller passed throughout, because each built the
/// command the same five-argument way. So these drive the REAL controller and read back the command
/// it actually sends, and deserialise the body with the options MVC uses.
/// </remarks>
public sealed class DealerProfileEndpointTests
{
    /// <summary>What MVC reads a request body with here: nothing in the API customises it.</summary>
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly Guid CityId = Guid.CreateVersion7();
    private static readonly Id Owner = Id.New();

    private static IReadOnlyList<DealersController.DayScheduleRequest> Week() =>
    [
        new("Sunday", false, "08:00", "20:00"),
        new("Monday", false, "08:00", "20:00"),
        new("Tuesday", false, "08:00", "20:00"),
        new("Wednesday", false, "08:00", "20:00"),
        new("Thursday", false, "08:00", "20:00"),
        new("Friday", false, "14:00", "20:00"),
        new("Saturday", true, null, null),
    ];

    /// <summary>The body as the console writes it, the location spelt the way the application form spells it.</summary>
    private static JsonObject Body() => new()
    {
        ["businessName"] = "Petra Rentals",
        ["latitude"] = 31.95,
        ["longitude"] = 35.91,
        ["operatingHours"] = new JsonArray(),
        ["cityId"] = CityId.ToString(),
        ["addressArea"] = "Abdoun",
        ["addressStreet"] = "Zahran Street",
    };

    private static DealersController.UpdateProfileRequest Read(JsonObject body) =>
        JsonSerializer.Deserialize<DealersController.UpdateProfileRequest>(body.ToJsonString(), Web)!;

    /// <summary>The real controller over a substituted mediator, keeping every request it is sent.</summary>
    private static (DealersController Controller, List<IRequest<Result<DealerProfileDto, Error>>> Sent) Controller()
    {
        var sent = new List<IRequest<Result<DealerProfileDto, Error>>>();
        var mediator = Substitute.For<ISender>();
        mediator
            .Send(Arg.Do<IRequest<Result<DealerProfileDto, Error>>>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Result.Success<DealerProfileDto, Error>(DealerProfileDto.From(Build.ApprovedDealer())));

        var actor = Substitute.For<ICurrentActor>();
        actor.UserId.Returns(Owner);

        var services = new ServiceCollection();
        services.AddSingleton(mediator);

        var controller = new DealersController(actor)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() },
            },
        };
        return (controller, sent);
    }

    [Fact]
    public async Task The_location_on_the_request_is_the_location_on_the_command()
    {
        var (controller, sent) = Controller();

        await controller.UpdateProfile(
            new DealersController.UpdateProfileRequest(
                "Petra Rentals", 31.95, 35.91, Week(), CityId, "Abdoun", "Zahran Street"),
            CancellationToken.None);

        var command = Assert.IsType<UpdateDealerProfileCommand>(Assert.Single(sent));
        Assert.Equal(Owner, command.OwnerUserId);
        Assert.Equal(Id.From(CityId), command.CityId);
        Assert.Equal("Abdoun", command.AddressArea);
        Assert.Equal("Zahran Street", command.AddressStreet);
        Assert.Equal(7, command.OperatingHours.Count);
        Assert.Equal("14:00", command.OperatingHours.Single(day => day.Day == "Friday").OpensAt);
    }

    [Fact]
    public async Task A_request_that_states_no_location_sends_none_and_the_handler_decides()
    {
        var (controller, sent) = Controller();

        await controller.UpdateProfile(
            new DealersController.UpdateProfileRequest("Petra Rentals", 31.95, 35.91, Week(), null, null, null),
            CancellationToken.None);

        var command = Assert.IsType<UpdateDealerProfileCommand>(Assert.Single(sent));
        Assert.Null(command.CityId);
        Assert.Null(command.AddressArea);
        Assert.Null(command.AddressStreet);
    }

    [Fact]
    public void The_location_travels_under_the_names_the_application_form_uses()
    {
        var request = Read(Body());

        Assert.Equal(CityId, request.CityId);
        Assert.Equal("Abdoun", request.AddressArea);
        Assert.Equal("Zahran Street", request.AddressStreet);
    }

    [Theory]
    [InlineData("cityId")]
    [InlineData("addressArea")]
    [InlineData("addressStreet")]
    public void A_body_that_leaves_out_part_of_the_location_is_refused_before_it_can_erase_anything(string left)
    {
        // A client that does not know about the location — the console before this fix, or any stale
        // build — used to send none, and the save erased the office's. Now leaving one out is a 400.
        // Exactly JsonException: an InvalidOperationException here would mean the runtime rejected
        // the attribute, and the endpoint would answer 500 to every request.
        var body = Body();
        body.Remove(left);

        var refused = Assert.Throws<JsonException>(() => Read(body));

        Assert.Contains(left, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_body_that_states_no_location_binds_as_none()
    {
        var body = Body();
        body["cityId"] = null;
        body["addressArea"] = null;
        body["addressStreet"] = null;

        var request = Read(body);

        Assert.Null(request.CityId);
        Assert.Null(request.AddressArea);
        Assert.Null(request.AddressStreet);
    }

    [Fact]
    public void Mvc_accepts_the_request_and_holds_its_address_to_the_domains_limits()
    {
        // A validation attribute on a record's PROPERTY, rather than its constructor parameter, makes
        // MVC throw while building metadata — which is every request to this action. `JsonRequired`
        // has to sit on the property and `StringLength` must not, so both are checked here.
        var services = new ServiceCollection();
        services.AddControllers();
        var metadata = services.BuildServiceProvider()
            .GetRequiredService<IModelMetadataProvider>()
            .GetMetadataForType(typeof(DealersController.UpdateProfileRequest));

        var parameters = metadata.BoundConstructor!.BoundConstructorParameters!;
        int Limit(string name) => parameters
            .Single(parameter => parameter.Name == name)
            .ValidatorMetadata.OfType<StringLengthAttribute>()
            .Single()
            .MaximumLength;

        Assert.Equal(DealerAddress.AreaMaxLength, Limit(nameof(DealersController.UpdateProfileRequest.AddressArea)));
        Assert.Equal(DealerAddress.StreetMaxLength, Limit(nameof(DealersController.UpdateProfileRequest.AddressStreet)));
        Assert.All(metadata.Properties, property => Assert.NotNull(property.PropertyName));
    }
}
