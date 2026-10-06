using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Fleet.BrowseCatalogue;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Fleet;

/// <summary>
/// A dated search lists only cars that can be handed over at the searched times (Wave 3 E7; E2E F2): from a counter
/// open at both the pickup and the return, or by delivery. Each office is judged by the rule the quote applies, so a
/// car the search lists is one the quote prices. An undated search is unchanged.
/// </summary>
public sealed class SearchByOpeningHoursTests
{
    private readonly ICatalogueReader _catalogue = Substitute.For<ICatalogueReader>();
    private static readonly Id EarlyOffice = Id.New();
    private static readonly Id LateOffice = Id.New();

    // Build.Now is 2026-09-03 10:00 UTC. The pickup is the next day at 09:00 Amman (06:00 UTC), the return two days
    // later at 12:00 Amman: well inside every window the booking rules set.
    private static readonly DateTimeOffset PickupAt = new(2026, 9, 4, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReturnAt = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    private static OperatingHours Hours(int opens, int closes) =>
        OperatingHours.Uniform(new TimeOnly(opens, 0), new TimeOnly(closes, 0)).Value;

    private SearchCatalogueHandler Handler() => new(
        _catalogue,
        Substitute.For<IGalleryReviewReader>(),
        TestBusinessRules.Provider(),
        TestBusinessRules.Calendar(),
        new TestClock(Build.Now));

    private static SearchCatalogueQuery Query(DateTimeOffset? pickupAt, DateTimeOffset? returnAt) =>
        new(null, null, null, null, null, null, null, false, null, pickupAt, returnAt, PageRequest.From(1, 20));

    private static CatalogueListing Car(Id office) => new(
        Guid.NewGuid(), "Kia", "Sportage", 2024, null, "Automatic", "Petrol", 5, null, new MoneyDto(30m, "JOD"), true,
        new CatalogueGalleryLabel(office.Value, "Office", null, null, null, 0));

    private void GivenOffices(params OfficeSchedule[] offices)
    {
        _catalogue.OfficeSchedulesAsync(Arg.Any<Id?>(), Arg.Any<Id?>(), Arg.Any<CancellationToken>()).Returns(offices);
        _catalogue.SearchAsync(Arg.Any<CatalogueFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<CatalogueListing>([Car(EarlyOffice), Car(LateOffice)], 1, 20, 2));
    }

    [Fact]
    public async Task An_office_open_at_both_ends_collects_and_one_shut_at_pickup_only_delivers()
    {
        GivenOffices(
            new OfficeSchedule(EarlyOffice, Hours(8, 20), DeliveryEnabled: false),
            new OfficeSchedule(LateOffice, Hours(10, 22), DeliveryEnabled: true));

        var page = await Handler().Handle(Query(PickupAt, ReturnAt), CancellationToken.None);

        await _catalogue.Received(1).SearchAsync(
            Arg.Is<CatalogueFilter>(filter =>
                filter.Collection != null &&
                filter.Collection.OpenForSelfPickup.SequenceEqual(new[] { EarlyOffice }) &&
                filter.Collection.Delivering.SequenceEqual(new[] { LateOffice })),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
        Assert.Equal([true, false], page.Value.Items.Select(item => item.SelfPickupAvailable));
    }

    [Fact]
    public async Task A_counter_that_closes_before_the_return_cannot_hand_the_car_over_for_self_pickup()
    {
        // Open at the 09:00 pickup, shut by the 12:00 return: the customer could take the car and not bring it back.
        GivenOffices(new OfficeSchedule(EarlyOffice, Hours(7, 11), DeliveryEnabled: false));

        await Handler().Handle(Query(PickupAt, ReturnAt), CancellationToken.None);

        await _catalogue.Received(1).SearchAsync(
            Arg.Is<CatalogueFilter>(filter => filter.Collection != null && filter.Collection.OpenForSelfPickup.Count == 0),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_undated_search_asks_about_no_hours_and_marks_nothing()
    {
        _catalogue.SearchAsync(Arg.Any<CatalogueFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<CatalogueListing>([Car(EarlyOffice)], 1, 20, 1));

        var page = await Handler().Handle(Query(null, null), CancellationToken.None);

        await _catalogue.DidNotReceiveWithAnyArgs().OfficeSchedulesAsync(default, default, default);
        await _catalogue.Received(1).SearchAsync(
            Arg.Is<CatalogueFilter>(filter => filter.Collection == null), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>());
        Assert.Null(Assert.Single(page.Value.Items).SelfPickupAvailable);
    }
}
