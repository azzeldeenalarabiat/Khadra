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
        // BOTH languages of every section, because fourteen columns is fourteen chances for one of
        // them to be silently dropped — which is exactly what the mapping comments in
        // `DealerConfiguration` warn about for a get-only property left to convention.
        var profile = PublicProfile.Create(
            Build.Both("ممنوع التدخين.", "No smoking."),
            Build.Both("شامل، بتحمل 200 دينار.", "Comprehensive, 200 JOD excess."),
            Build.Both("أحضر الرخصة الأصلية.", "Bring the original licence."),
            Build.Both("نوصل إلى المطار.", "We deliver to the airport."),
            Build.Both("اسأل عن رامي.", "Ask for Rami."),
            ["About", "Insurance"]).Value;

        var dealerId = await GivenDealerAsync(dealer =>
            Assert.True(dealer.UpdatePublicProfile(
                Build.Both("مكتب عائلي منذ 2014.", "Family-run since 2014."), profile).IsSuccess));

        await using var reader = NewContext();
        var stored = await reader.Dealers.SingleAsync(dealer => dealer.Id == dealerId);

        Assert.Equal("Family-run since 2014.", stored.Description.En);
        Assert.Equal("No smoking.", stored.PublicProfile.RentalConditions.En);
        Assert.Equal("Comprehensive, 200 JOD excess.", stored.PublicProfile.Insurance.En);
        Assert.Equal("Bring the original licence.", stored.PublicProfile.PickupInstructions.En);
        Assert.Equal("We deliver to the airport.", stored.PublicProfile.DeliveryNotes.En);
        Assert.Equal("Ask for Rami.", stored.PublicProfile.CustomerNotes.En);
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
            // Exactly what an office that has written nothing has after the bilingual migration:
            // fourteen NULLs and the default '' for what is hidden.
            //
            // This is the row shape the whole flat-columns decision exists for. An owned type whose
            // columns are ALL nullable materialises as null, so `PublicProfile` would have been null
            // for every office in this state — which is most of them — and `Resolve` would have
            // thrown the moment anybody opened their page. The assertion below is the proof that it
            // does not.
            await migrated.Database.ExecuteSqlRawAsync(
                """
                update dealers
                   set rental_conditions_ar = null, rental_conditions_en = null,
                       insurance_summary_ar = null, insurance_summary_en = null,
                       pickup_instructions_ar = null, pickup_instructions_en = null,
                       delivery_notes_ar = null, delivery_notes_en = null,
                       customer_notes_ar = null, customer_notes_en = null,
                       description_ar = null, description_en = null,
                       hidden_profile_sections = ''
                """);
        }

        await using var reader = NewContext();
        var stored = await reader.Dealers.SingleAsync(dealer => dealer.Id == dealerId);

        // Not null: the page is empty, and every section of it is simply unwritten.
        Assert.NotNull(stored.PublicProfile);
        Assert.Empty(stored.PublicProfile.HiddenSections);
        Assert.Null(stored.PublicProfile.RentalConditions.En);
        Assert.Null(stored.VisiblePublicProfile(Language.English).About);
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
                Build.En("About us."),
                PublicProfile.Create(default, default, default, default, default, ["Insurance"]).Value).IsSuccess));

        await using var reader = NewContext();
        var stored = await reader.Dealers.SingleAsync(dealer => dealer.Id == dealerId);

        Assert.Single(stored.PublicProfile.HiddenSections);
        Assert.False(reader.ChangeTracker.HasChanges());
    }
}
