using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Application.Dealers.CustomerPage;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Dealers;

/// <summary>
/// The console's editor for what a rental office tells its customers.
/// </summary>
/// <remarks>
/// Two things are the point of this endpoint rather than of the screen. The PREVIEW is the server's
/// own answer to "what does a customer see", so a console cannot re-implement "hidden or empty" and
/// drift from the page. And the SECTIONS it offers are the platform's vocabulary, so a console cannot
/// offer to hide something the platform does not let an office hide — the opening hours a pickup is
/// held to, for instance.
/// </remarks>
public sealed class DealerCustomerPageTests
{
    private static readonly Id OwnerId = Id.New();

    private sealed class Context
    {
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

        public Context() => UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        public Dealer Given(Dealer dealer)
        {
            Dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);
            return dealer;
        }

        public Id GivenEmployeeOf(Dealer dealer)
        {
            var staffId = Id.New();
            dealer.HireEmployee(staffId, canViewReports: false, Build.Now);
            Dealers.GetByStaffUserIdAsync(staffId, Arg.Any<CancellationToken>()).Returns(dealer);
            return staffId;
        }

        public DealerCustomerPageHandlers Handlers() =>
            new(new DealerMembershipResolver(Dealers), UnitOfWork);
    }

    private static UpdateDealerCustomerPageCommand Page(
        Id ownerUserId,
        LocalizedInput about = default,
        LocalizedInput rentalConditions = default,
        LocalizedInput insurance = default,
        LocalizedInput pickupInstructions = default,
        LocalizedInput deliveryNotes = default,
        LocalizedInput customerNotes = default,
        IReadOnlyList<string>? hidden = null) =>
        new(ownerUserId, about, rentalConditions, insurance, pickupInstructions, deliveryNotes,
            customerNotes, hidden);

    [Fact]
    public async Task The_owner_writes_the_page_and_is_shown_what_a_customer_would_see()
    {
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));

        var result = await context.Handlers().Handle(
            Page(
                OwnerId,
                about: Build.En("Family-run since 2014."),
                rentalConditions: Build.En("No smoking."),
                insurance: Build.En("Comprehensive, 200 JOD excess."),
                hidden: ["Insurance"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // What the office wrote comes back whole, hidden or not: it is still theirs to show again.
        Assert.Equal("Comprehensive, 200 JOD excess.", result.Value.Insurance.En);
        Assert.Equal(["Insurance"], result.Value.HiddenSections);
        // What a customer sees is the server's answer, not the console's.
        Assert.Equal("Family-run since 2014.", result.Value.Visible.En.About!.Text);
        Assert.Equal("No smoking.", result.Value.Visible.En.RentalConditions!.Text);
        Assert.Null(result.Value.Visible.En.Insurance);
        Assert.Equal("Family-run since 2014.", dealer.Description.En);
    }

    [Fact]
    public async Task The_editor_offers_the_platforms_sections_and_its_own_limit()
    {
        var context = new Context();
        context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));

        var page = await context.Handlers().Handle(
            new GetDealerCustomerPageQuery(OwnerId), CancellationToken.None);

        Assert.True(page.IsSuccess);
        // The six the platform has, and nothing a console could invent beside them — the opening
        // hours, the delivery radius and the rating are not an office's to hide.
        Assert.Equal(
            Enumeration.GetAll<PublicProfileSection>().Select(section => section.Name),
            page.Value.Sections);
        Assert.Equal(ProfileText.MaxLength, page.Value.MaxTextLength);
        Assert.Empty(page.Value.HiddenSections);
    }

    [Fact]
    public async Task Delivery_notes_say_whether_delivery_is_even_on()
    {
        // An owner typing into a box whose text no customer will see deserves to be told why.
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));

        var off = await context.Handlers().Handle(
            Page(OwnerId, deliveryNotes: Build.En("We deliver to the airport.")), CancellationToken.None);

        Assert.False(off.Value.DeliveryEnabled);
        Assert.Equal("We deliver to the airport.", off.Value.DeliveryNotes.En);
        Assert.Null(off.Value.Visible.En.DeliveryNotes);
        Assert.Null(off.Value.Visible.Ar.DeliveryNotes);

        Assert.True(dealer.EnableDelivery(10m, Money.Jod(5m), Build.Now).IsSuccess);
        var on = await context.Handlers().Handle(
            new GetDealerCustomerPageQuery(OwnerId), CancellationToken.None);

        Assert.True(on.Value.DeliveryEnabled);
        Assert.Equal("We deliver to the airport.", on.Value.Visible.En.DeliveryNotes!.Text);
    }

    [Fact]
    public async Task A_save_replaces_the_whole_page()
    {
        var context = new Context();
        context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));
        await context.Handlers().Handle(
            Page(OwnerId, about: Build.En("About us."), insurance: Build.En("Comprehensive.")), CancellationToken.None);

        var second = await context.Handlers().Handle(
            Page(OwnerId, rentalConditions: Build.En("No smoking.")), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.About.Ar is null && second.Value.About.En is null);
        Assert.Null(second.Value.Insurance.En);
        Assert.Equal("No smoking.", second.Value.RentalConditions.En);
    }

    [Fact]
    public async Task An_employee_reads_the_page_and_cannot_write_it()
    {
        // Spec 4.2: staff answer bookings; what the platform tells customers about the office is the
        // owner's to decide.
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));
        Assert.True(dealer.UpdatePublicProfile(Build.En("About us."), PublicProfile.Empty()).IsSuccess);
        var staffId = context.GivenEmployeeOf(dealer);

        var read = await context.Handlers().Handle(
            new GetDealerCustomerPageQuery(staffId), CancellationToken.None);
        var written = await context.Handlers().Handle(
            Page(staffId, about: Build.En("Mine now.")), CancellationToken.None);

        Assert.True(read.IsSuccess);
        Assert.Equal("About us.", read.Value.About.En);
        Assert.Equal("dealer.owner_only", written.Error.Code);
        Assert.Equal("About us.", dealer.Description.En);
    }

    [Fact]
    public async Task A_section_the_platform_does_not_have_is_refused_and_nothing_is_saved()
    {
        var context = new Context();
        var dealer = context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));

        var result = await context.Handlers().Handle(
            Page(OwnerId, about: Build.En("About us."), hidden: ["OpeningHours"]), CancellationToken.None);

        Assert.Equal("dealer.unknown_profile_section", result.Error.Code);
        Assert.Null(dealer.Description.En);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_section_longer_than_the_page_allows_is_refused_by_the_field_that_holds_it()
    {
        var context = new Context();
        context.Given(Build.ApprovedDealer(ownerUserId: OwnerId));

        var result = await context.Handlers().Handle(
            Page(OwnerId, insurance: Build.En(new string('x', ProfileText.MaxLength + 1))), CancellationToken.None);

        Assert.Equal("dealer.profile_text_too_long", result.Error.Code);

        // The BOX, not just the section. With two under one heading, "insurance is too long" leaves
        // an owner to work out which of the two they broke — and the untouched one must not be
        // blamed for it.
        Assert.True(result.Error.Details!.ContainsKey("insuranceEn"));
        Assert.False(result.Error.Details.ContainsKey("insuranceAr"));
    }

    [Fact]
    public async Task An_applicant_prepares_the_page_while_the_licence_is_still_being_checked()
    {
        var context = new Context();
        context.Given(Build.Dealer(ownerUserId: OwnerId));

        var result = await context.Handlers().Handle(
            Page(OwnerId, about: Build.En("Opening soon.")), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        // Nothing on it reaches a customer: the public page answers 404 for an office that cannot trade.
        Assert.Equal("Opening soon.", result.Value.Visible.En.About!.Text);
    }

    [Fact]
    public async Task An_account_with_no_dealership_is_told_so()
    {
        var context = new Context();

        var result = await context.Handlers().Handle(
            new GetDealerCustomerPageQuery(OwnerId), CancellationToken.None);

        Assert.Equal("dealer.not_registered", result.Error.Code);
    }
}
