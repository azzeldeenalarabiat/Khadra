using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Application.Dealers.UpdateProfile;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.PlatformSettings;
using Khadra.Domain.PlatformSettings.Repositories;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Dealers;

// Spec 4.1: the owner keeps the dealer page current. Held hardest: the name the licence was verified
// against is locked after approval, branding lives under its own storage prefix (never the one that
// holds licence scans), and only the owner may do any of it.
public sealed class DealerProfileTests
{
    private static readonly Id OwnerId = Id.New();

    private sealed class Context
    {
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IUploadTicketService Uploads { get; } = Substitute.For<IUploadTicketService>();
        public IDocumentStorage Storage { get; } = Substitute.For<IDocumentStorage>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public TestClock Clock { get; } = new(Build.Now);
        public List<string> Deleted { get; } = [];

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Uploads.Issue(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>())
                .Returns(call => new UploadTicket("/api/v1/uploads/t", call.ArgAt<string>(0), Build.Now.AddMinutes(15)));
            Storage.OpenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])));
            Storage.When(storage => storage.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
                .Do(call => Deleted.Add(call.Arg<string>()));
        }

        public Dealer Given(Dealer dealer)
        {
            Dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);
            return dealer;
        }

        public ICityRepository Cities { get; } = Substitute.For<ICityRepository>();

        public DealerProfileHandlers Handlers() => new(
            new DealerMembershipResolver(Dealers), Cities, Uploads, Storage, FakeDocumentPolicy.Default, Clock, UnitOfWork);
    }

    private static IReadOnlyList<DayScheduleInput> Week(string fridayOpens = "14:00", bool saturdayClosed = true) =>
    [
        new("Sunday", false, "08:00", "20:00"),
        new("Monday", false, "08:00", "20:00"),
        new("Tuesday", false, "08:00", "20:00"),
        new("Wednesday", false, "08:00", "20:00"),
        new("Thursday", false, "08:00", "20:00"),
        new("Friday", false, fridayOpens, "20:00"),
        new("Saturday", saturdayClosed, null, null),
    ];

    /// <summary>
    /// A save that states the location the office already has — what the form sends when the owner
    /// changed something else. Every save states one: see <see cref="UpdateDealerProfileCommand"/>.
    /// </summary>
    private static UpdateDealerProfileCommand KeepingLocation(
        Dealer dealer, Id asUser, string businessName, IReadOnlyList<DayScheduleInput> hours) =>
        new(asUser, businessName, 31.95, 35.91, hours, dealer.CityId, dealer.Address?.Area, dealer.Address?.Street);

    /// <summary>The same office, filed under a city and an address, as submission would have left it.</summary>
    private static Dealer FiledUnder(Dealer dealer, Id cityId)
    {
        Assert.True(dealer.UpdateProfile(
            dealer.BusinessName,
            dealer.Location,
            dealer.OperatingHours,
            cityId,
            DealerAddress.Create("Abdoun", "Zahran Street").Value).IsSuccess);
        return dealer;
    }

    private static City OfferedCity(string name = "Amman") =>
        City.Create(name, "عمّان", 1, Build.Now).Value;

    private static City RetiredCity(string name = "Zarqa")
    {
        var city = City.Create(name, "الزرقاء", 2, Build.Now).Value;
        Assert.True(city.Deactivate().IsSuccess);
        return city;
    }

    [Fact]
    public async Task The_owner_updates_pin_and_seven_day_hours_and_the_location_and_About_text_are_left_alone()
    {
        var context = new Context();
        var cityId = Id.New();
        var dealer = context.Given(FiledUnder(Build.ApprovedDealer(ownerUserId: OwnerId), cityId));
        // Written on the customer page, which is its one writer now.
        Assert.True(dealer.UpdatePublicProfile("Family-run since 2014.", PublicProfile.Empty()).IsSuccess);

        var result = await context.Handlers().Handle(
            KeepingLocation(dealer, OwnerId, dealer.BusinessName.Value, Week()),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        // This form no longer carries it, so saving the form cannot overwrite it.
        Assert.Equal("Family-run since 2014.", dealer.Description);
        Assert.Equal(31.95, dealer.Location.Latitude, 4);
        Assert.True(dealer.OperatingHours.For(DayOfWeek.Saturday).IsClosed);
        Assert.Equal(new TimeOnly(14, 0), dealer.OperatingHours.For(DayOfWeek.Friday).OpensAt);
        Assert.Equal(7, result.Value.OperatingHours.Count);
        Assert.Equal("14:00", result.Value.OperatingHours.Single(day => day.Day == "Friday").OpensAt);
        Assert.True(result.Value.IsOwner);
        // What this test used to leave unasserted, and what every save used to erase.
        Assert.Equal(cityId, dealer.CityId);
        Assert.Equal("Abdoun", dealer.Address?.Area);
        Assert.Equal("Zahran Street", dealer.Address?.Street);
        Assert.Equal(cityId.Value, result.Value.CityId);
        Assert.Equal("Abdoun", result.Value.Address?.Area);
        Assert.Equal("Zahran Street", result.Value.Address?.Street);
    }

    [Fact]
    public async Task An_office_keeps_a_city_that_has_since_been_retired_without_the_lookup_being_asked()
    {
        // Retiring a city must not stop an office filed under it from saving its hours, nor force it
        // to move to save anything at all. The id is unchanged, so the lookup is not consulted.
        var context = new Context();
        var retired = RetiredCity();
        context.Cities.GetByIdAsync(retired.Id, Arg.Any<CancellationToken>()).Returns(retired);
        var dealer = context.Given(FiledUnder(Build.ApprovedDealer(ownerUserId: OwnerId), retired.Id));

        var result = await context.Handlers().Handle(
            KeepingLocation(dealer, OwnerId, dealer.BusinessName.Value, Week(fridayOpens: "15:00")),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(retired.Id, dealer.CityId);
        Assert.Equal(new TimeOnly(15, 0), dealer.OperatingHours.For(DayOfWeek.Friday).OpensAt);
        await context.Cities.DidNotReceive().GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moving_to_an_offered_city_is_accepted_and_checked_against_the_lookup()
    {
        var context = new Context();
        var offered = OfferedCity();
        context.Cities.GetByIdAsync(offered.Id, Arg.Any<CancellationToken>()).Returns(offered);
        var dealer = context.Given(FiledUnder(Build.ApprovedDealer(ownerUserId: OwnerId), Id.New()));

        var result = await context.Handlers().Handle(
            new UpdateDealerProfileCommand(
                OwnerId, dealer.BusinessName.Value, 31.95, 35.91, Week(), offered.Id, "Sweifieh", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(offered.Id, dealer.CityId);
        Assert.Equal("Sweifieh", dealer.Address?.Area);
        Assert.Null(dealer.Address?.Street);
        await context.Cities.Received(1).GetByIdAsync(offered.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moving_to_a_retired_city_or_one_that_does_not_exist_is_refused_and_nothing_moves()
    {
        var context = new Context();
        var retired = RetiredCity();
        var nowhere = Id.New();
        context.Cities.GetByIdAsync(retired.Id, Arg.Any<CancellationToken>()).Returns(retired);
        context.Cities.GetByIdAsync(nowhere, Arg.Any<CancellationToken>()).Returns((City?)null);
        var home = Id.New();
        var dealer = context.Given(FiledUnder(Build.ApprovedDealer(ownerUserId: OwnerId), home));

        var toRetired = await context.Handlers().Handle(
            new UpdateDealerProfileCommand(
                OwnerId, dealer.BusinessName.Value, 31.95, 35.91, Week(), retired.Id, "Abdoun", "Zahran Street"),
            CancellationToken.None);
        var toNowhere = await context.Handlers().Handle(
            new UpdateDealerProfileCommand(
                OwnerId, dealer.BusinessName.Value, 31.95, 35.91, Week(), nowhere, "Abdoun", "Zahran Street"),
            CancellationToken.None);

        Assert.Equal("dealer.unknown_city", toRetired.Error.Code);
        Assert.Equal("dealer.unknown_city", toNowhere.Error.Code);
        Assert.Equal(home, dealer.CityId);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_address_is_refused_rather_than_cut_when_it_runs_past_its_limits()
    {
        var context = new Context();
        var dealer = context.Given(FiledUnder(Build.ApprovedDealer(ownerUserId: OwnerId), Id.New()));

        async Task<string?> Saving(string? area, string? street) =>
            (await context.Handlers().Handle(
                new UpdateDealerProfileCommand(
                    OwnerId, dealer.BusinessName.Value, 31.95, 35.91, Week(), dealer.CityId, area, street),
                CancellationToken.None)) is { IsFailure: true } failed ? failed.Error.Code : null;

        Assert.Equal("dealer.address_area_too_long", await Saving(new string('a', DealerAddress.AreaMaxLength + 1), null));
        Assert.Equal("dealer.address_street_too_long", await Saving("Abdoun", new string('s', DealerAddress.StreetMaxLength + 1)));
        // A street is somewhere only inside an area; many have no name, so the reverse is fine.
        Assert.Equal("dealer.invalid_address_area", await Saving(null, "Zahran Street"));
        Assert.Null(await Saving(new string('a', DealerAddress.AreaMaxLength), new string('s', DealerAddress.StreetMaxLength)));
    }

    [Fact]
    public async Task Stating_no_address_and_no_city_clears_them()
    {
        // The API's rule, unchanged from submission: null is an answer. The console does not offer
        // "no city" once an office has one, but the contract still means what it says.
        var context = new Context();
        var dealer = context.Given(FiledUnder(Build.ApprovedDealer(ownerUserId: OwnerId), Id.New()));

        var result = await context.Handlers().Handle(
            new UpdateDealerProfileCommand(OwnerId, dealer.BusinessName.Value, 31.95, 35.91, Week(), null, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Null(dealer.CityId);
        Assert.Null(dealer.Address);
    }

    [Fact]
    public void The_validator_holds_the_address_to_the_same_limits_as_the_domain()
    {
        var validator = new UpdateDealerProfileCommandValidator();
        UpdateDealerProfileCommand With(string? area, string? street) =>
            new(OwnerId, "Petra Rentals", 31.95, 35.91, Week(), Id.New(), area, street);

        Assert.False(validator.Validate(With(new string('a', DealerAddress.AreaMaxLength + 1), null)).IsValid);
        Assert.False(validator.Validate(With("Abdoun", new string('s', DealerAddress.StreetMaxLength + 1))).IsValid);
        Assert.True(validator.Validate(With(new string('a', DealerAddress.AreaMaxLength), new string('s', DealerAddress.StreetMaxLength))).IsValid);
    }

    [Fact]
    public async Task The_business_name_is_locked_once_approved_but_free_before()
    {
        var context = new Context();
        var approved = context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));

        var renamed = await context.Handlers().Handle(
            KeepingLocation(approved, OwnerId, "Al-Nadeem Premium Rentals", Week()),
            CancellationToken.None);
        Assert.Equal("dealer.business_name_locked", renamed.Error.Code);

        var applicant = context.Given(Build.Dealer(ownerUserId: OwnerId));
        var fixedUp = await context.Handlers().Handle(
            KeepingLocation(applicant, OwnerId, "Al-Nadeem Rentals (corrected)", Week()),
            CancellationToken.None);
        Assert.True(fixedUp.IsSuccess, fixedUp.IsFailure ? fixedUp.Error.Code : null);
        Assert.Equal("Al-Nadeem Rentals (corrected)", applicant.BusinessName.Value);
    }

    [Fact]
    public async Task Hours_that_close_before_they_open_are_refused_as_a_field_error()
    {
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));

        var result = await context.Handlers().Handle(
            KeepingLocation(dealer, OwnerId, dealer.BusinessName.Value, Week(fridayOpens: "22:00")),
            CancellationToken.None);

        Assert.Equal("dealer.invalid_operating_hours", result.Error.Code);
    }

    [Fact]
    public async Task Branding_uploads_land_under_the_dealers_own_public_prefix_never_the_document_one()
    {
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));

        var logo = await context.Handlers().Handle(
            new RequestBrandingUploadCommand(OwnerId, "logo", "image/png"), CancellationToken.None);
        var pdf = await context.Handlers().Handle(
            new RequestBrandingUploadCommand(OwnerId, "cover", "application/pdf"), CancellationToken.None);
        var banner = await context.Handlers().Handle(
            new RequestBrandingUploadCommand(OwnerId, "banner", "image/png"), CancellationToken.None);

        Assert.StartsWith($"dealer-branding/{dealer.Id.Value}/logo-", logo.Value.StorageKey, StringComparison.Ordinal);
        Assert.DoesNotContain($"dealers/{dealer.Id.Value}/", logo.Value.StorageKey, StringComparison.Ordinal);
        Assert.Equal("dealer.invalid_branding_type", pdf.Error.Code);
        Assert.Equal("dealer.invalid_branding_kind", banner.Error.Code);
    }

    [Fact]
    public async Task Setting_a_new_logo_replaces_the_old_file_only_after_the_save()
    {
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));
        var first = $"dealer-branding/{dealer.Id.Value}/logo-1.png";
        var second = $"dealer-branding/{dealer.Id.Value}/logo-2.png";

        var one = await context.Handlers().Handle(new SetBrandingCommand(OwnerId, "logo", first), CancellationToken.None);
        var two = await context.Handlers().Handle(new SetBrandingCommand(OwnerId, "logo", second), CancellationToken.None);

        Assert.True(one.IsSuccess && two.IsSuccess);
        Assert.Equal(second, dealer.LogoStorageKey);
        Assert.Equal([first], context.Deleted);
        Assert.Equal($"/api/v1/dealer-images/{second}", two.Value.LogoUrl);
        Assert.Null(two.Value.CoverUrl);
    }

    [Fact]
    public async Task A_key_outside_the_dealers_prefix_or_with_no_bytes_behind_it_is_refused()
    {
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));
        var foreign = $"dealer-branding/{Guid.NewGuid()}/logo-x.png";
        var documents = $"dealers/{dealer.Id.Value}/CommercialRegistration.pdf";
        var missing = $"dealer-branding/{dealer.Id.Value}/logo-missing.png";
        context.Storage.OpenAsync(missing, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Stream?>(null));

        Assert.Equal("dealer.branding_outside_dealer",
            (await context.Handlers().Handle(new SetBrandingCommand(OwnerId, "logo", foreign), CancellationToken.None)).Error.Code);
        // The one that matters: a licence scan can never be promoted to a public logo.
        Assert.Equal("dealer.branding_outside_dealer",
            (await context.Handlers().Handle(new SetBrandingCommand(OwnerId, "logo", documents), CancellationToken.None)).Error.Code);
        Assert.Equal("dealer.branding_not_uploaded",
            (await context.Handlers().Handle(new SetBrandingCommand(OwnerId, "logo", missing), CancellationToken.None)).Error.Code);
        Assert.Null(dealer.LogoStorageKey);
    }

    [Fact]
    public async Task An_employee_cannot_edit_the_dealer_page()
    {
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));
        var staffId = Id.New();
        dealer.HireEmployee(staffId, false, Build.Now);
        context.Dealers.GetByStaffUserIdAsync(staffId, Arg.Any<CancellationToken>()).Returns(dealer);

        var result = await context.Handlers().Handle(
            KeepingLocation(dealer, staffId, dealer.BusinessName.Value, Week()),
            CancellationToken.None);

        Assert.Equal("dealer.owner_only", result.Error.Code);
    }
}
