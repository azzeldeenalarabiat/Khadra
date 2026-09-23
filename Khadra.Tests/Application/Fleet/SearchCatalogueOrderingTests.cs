using Khadra.Application.Common;
using Khadra.Application.Fleet.BrowseCatalogue;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Fleet;

/// <summary>
/// The search's new inputs (2026-09-23): an order nobody offered, or a year range that could match
/// nothing, is refused as a client bug rather than answered with something the customer did not ask
/// for — and a request without them means exactly what it meant before.
/// </summary>
public sealed class SearchCatalogueOrderingTests
{
    private readonly ICatalogueReader _catalogue = Substitute.For<ICatalogueReader>();

    private SearchCatalogueHandler Handler() => new(
        _catalogue, TestBusinessRules.Provider(), TestBusinessRules.Calendar(), new TestClock(Build.Now));

    private static SearchCatalogueQuery Query(string? sort = null, int? minYear = null, int? maxYear = null) =>
        new(null, null, null, null, null, null, null, false, null, null, null, PageRequest.From(1, 20),
            Sort: sort, MinYear: minYear, MaxYear: maxYear);

    [Theory]
    [InlineData("Newest", "Newest")]
    [InlineData("pricelowtohigh", "PriceLowToHigh")]
    [InlineData("PRICEHIGHTOLOW", "PriceHighToLow")]
    [InlineData("YearNewest", "YearNewest")]
    public async Task A_named_order_reaches_the_reader_whatever_its_case(string asked, string expected)
    {
        await Handler().Handle(Query(sort: asked), CancellationToken.None);

        await _catalogue.Received(1).SearchAsync(
            Arg.Is<CatalogueFilter>(filter => filter.Sort != null && filter.Sort.Name == expected),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_order_means_the_readers_default_as_before()
    {
        await Handler().Handle(Query(), CancellationToken.None);

        await _catalogue.Received(1).SearchAsync(
            Arg.Is<CatalogueFilter>(filter => filter.Sort == null),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_order_nobody_offered_is_refused()
    {
        var result = await Handler().Handle(Query(sort: "cheapest"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("catalogue.unknown_sort", result.Error.Code);
        await _catalogue.DidNotReceiveWithAnyArgs().SearchAsync(default!, default!, default);
    }

    [Fact]
    public async Task A_year_range_that_ends_before_it_starts_is_refused()
    {
        var result = await Handler().Handle(Query(minYear: 2024, maxYear: 2020), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("catalogue.inverted_years", result.Error.Code);
    }
}
