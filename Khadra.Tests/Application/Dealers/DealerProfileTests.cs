using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Application.Dealers.UpdateProfile;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
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

        public DealerProfileHandlers Handlers() => new(
            new DealerMembershipResolver(Dealers), Uploads, Storage, FakeDocumentPolicy.Default, Clock, UnitOfWork);
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

    [Fact]
    public async Task The_owner_updates_description_pin_and_seven_day_hours()
    {
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));

        var result = await context.Handlers().Handle(
            new UpdateDealerProfileCommand(OwnerId, dealer.BusinessName.Value, "Family-run since 2014.", 31.95, 35.91, Week()),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal("Family-run since 2014.", dealer.Description);
        Assert.Equal(31.95, dealer.Location.Latitude, 4);
        Assert.True(dealer.OperatingHours.For(DayOfWeek.Saturday).IsClosed);
        Assert.Equal(new TimeOnly(14, 0), dealer.OperatingHours.For(DayOfWeek.Friday).OpensAt);
        Assert.Equal(7, result.Value.OperatingHours.Count);
        Assert.Equal("14:00", result.Value.OperatingHours.Single(day => day.Day == "Friday").OpensAt);
        Assert.True(result.Value.IsOwner);
    }

    [Fact]
    public async Task The_business_name_is_locked_once_approved_but_free_before()
    {
        var context = new Context();
        var approved = context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));

        var renamed = await context.Handlers().Handle(
            new UpdateDealerProfileCommand(OwnerId, "Al-Nadeem Premium Rentals", null, 31.95, 35.91, Week()),
            CancellationToken.None);
        Assert.Equal("dealer.business_name_locked", renamed.Error.Code);

        var applicant = context.Given(Build.Dealer(ownerUserId: OwnerId));
        var fixedUp = await context.Handlers().Handle(
            new UpdateDealerProfileCommand(OwnerId, "Al-Nadeem Rentals (corrected)", null, 31.95, 35.91, Week()),
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
            new UpdateDealerProfileCommand(OwnerId, dealer.BusinessName.Value, null, 31.95, 35.91, Week(fridayOpens: "22:00")),
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
            new UpdateDealerProfileCommand(staffId, dealer.BusinessName.Value, "Mine now.", 31.95, 35.91, Week()),
            CancellationToken.None);

        Assert.Equal("dealer.owner_only", result.Error.Code);
        Assert.NotEqual("Mine now.", dealer.Description);
    }
}
