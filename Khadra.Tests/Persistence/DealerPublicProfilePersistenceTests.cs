using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The customer page, through the real EF model.
/// </summary>
/// <remarks>
/// Owned types on this platform have a history of being written and never stored: every property is
/// get-only, EF includes one by convention only when it has a setter, and anything left to convention
/// is dropped with no error at all. So each of the six fields is asserted after a real round trip,
/// through a second context that has to build the value from its columns.
///
/// The other half is the rows that already exist. The migration gives them
/// <c>hidden_profile_sections = ''</c> and five NULLs, and an owned type whose columns are all
/// nullable materialises as NULL — which would make `Dealer.PublicProfile` null for every office
/// registered before this feature, and throw the moment anybody opened their page.
/// </remarks>
public sealed class DealerPublicProfilePersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public DealerPublicProfilePersistenceTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private KhadraDbContext NewContext() => new(_options);

    private async Task<Id> GivenDealerAsync(Action<Dealer>? write = null)
    {
        var dealer = Build.ApprovedDealer();
        write?.Invoke(dealer);

        await using var context = NewContext();
        context.Dealers.Add(dealer);
        await context.SaveChangesAsync();
        return dealer.Id;
    }

    [Fact]
    public async Task Every_section_and_the_hidden_set_survive_the_round_trip()
    {
        var profile = PublicProfile.Create(
            "No smoking.",
            "Comprehensive, 200 JOD excess.",
            "Bring the original licence.",
            "We deliver to the airport.",
            "Ask for Rami.",
            ["About", "Insurance"]).Value;

        var dealerId = await GivenDealerAsync(dealer =>
            Assert.True(dealer.UpdatePublicProfile("Family-run since 2014.", profile).IsSuccess));

        await using var reader = NewContext();
        var stored = await reader.Dealers.SingleAsync(dealer => dealer.Id == dealerId);

        Assert.Equal("Family-run since 2014.", stored.Description);
        Assert.Equal("No smoking.", stored.PublicProfile.RentalConditions);
        Assert.Equal("Comprehensive, 200 JOD excess.", stored.PublicProfile.Insurance);
        Assert.Equal("Bring the original licence.", stored.PublicProfile.PickupInstructions);
        Assert.Equal("We deliver to the airport.", stored.PublicProfile.DeliveryNotes);
        Assert.Equal("Ask for Rami.", stored.PublicProfile.CustomerNotes);
        Assert.True(stored.PublicProfile.IsHidden(PublicProfileSection.About));
        Assert.True(stored.PublicProfile.IsHidden(PublicProfileSection.Insurance));
        Assert.Equal(2, stored.PublicProfile.HiddenSections.Count);
    }

    [Fact]
    public async Task A_row_shaped_the_way_the_migration_leaves_it_reads_as_an_empty_page()
    {
        var dealerId = await GivenDealerAsync();

        await using (var migrated = NewContext())
        {
            // Exactly what an office registered before the customer page existed has after the
            // migration: nothing written, and the default '' for what is hidden.
            await migrated.Database.ExecuteSqlRawAsync(
                """
                update dealers
                   set rental_conditions = null,
                       insurance_summary = null,
                       pickup_instructions = null,
                       delivery_notes = null,
                       customer_notes = null,
                       hidden_profile_sections = ''
                """);
        }

        await using var reader = NewContext();
        var stored = await reader.Dealers.SingleAsync(dealer => dealer.Id == dealerId);

        // Not null: the page is empty, and every section of it is simply unwritten.
        Assert.NotNull(stored.PublicProfile);
        Assert.Empty(stored.PublicProfile.HiddenSections);
        Assert.Null(stored.PublicProfile.RentalConditions);
        Assert.Null(stored.VisiblePublicProfile().About);
    }

    [Fact]
    public async Task A_section_name_this_build_no_longer_knows_is_dropped_rather_than_thrown()
    {
        // Materialising a dealer must never fail over a stored string, and a section that no longer
        // exists has nothing left to hide.
        var dealerId = await GivenDealerAsync();

        await using (var tampered = NewContext())
        {
            await tampered.Database.ExecuteSqlRawAsync(
                "update dealers set hidden_profile_sections = 'About;Prices;Insurance'");
        }

        await using var reader = NewContext();
        var stored = await reader.Dealers.SingleAsync(dealer => dealer.Id == dealerId);

        Assert.Equal(2, stored.PublicProfile.HiddenSections.Count);
        Assert.True(stored.PublicProfile.IsHidden(PublicProfileSection.About));
        Assert.True(stored.PublicProfile.IsHidden(PublicProfileSection.Insurance));
    }

    [Fact]
    public async Task A_dealer_read_and_not_touched_has_nothing_to_save()
    {
        // The hidden set is rebuilt on every read, so without a comparer that looks at its CONTENT,
        // every loaded dealer would look modified and every read would end in a write.
        var dealerId = await GivenDealerAsync(dealer => Assert.True(
            dealer.UpdatePublicProfile(
                "About us.",
                PublicProfile.Create(null, null, null, null, null, ["Insurance"]).Value).IsSuccess));

        await using var reader = NewContext();
        var stored = await reader.Dealers.SingleAsync(dealer => dealer.Id == dealerId);

        Assert.Single(stored.PublicProfile.HiddenSections);
        Assert.False(reader.ChangeTracker.HasChanges());
    }
}
