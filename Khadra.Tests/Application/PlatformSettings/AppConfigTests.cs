using Khadra.Application.Common.Ports;
using Khadra.Application.PlatformSettings.AppConfig;
using Khadra.Domain.Common;
using Khadra.Domain.Fleet;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.PlatformSettings;

/// <summary>
/// The one call a client makes before it can ask the platform anything sensible.
/// </summary>
/// <remarks>
/// Everything here is a value the server owns and a phone would otherwise carry its own copy of.
/// The copy is the problem: a customer app is in shops, so a number baked into it needs a release
/// to change, and until every customer updates there are two answers to one question.
/// </remarks>
public sealed class AppConfigTests
{
    private static GetAppConfigHandler Handler(int? minimumRenterAge = 21)
    {
        var calendar = Substitute.For<IReportingCalendar>();
        calendar.TimeZoneId.Returns("Asia/Amman");

        var documents = Substitute.For<IDocumentPolicySettings>();
        documents.MaximumSizeBytes.Returns(8L * 1024 * 1024);
        documents.AllowedContentTypes.Returns(["image/jpeg", "image/png", "application/pdf"]);

        return new GetAppConfigHandler(
            calendar,
            TestBusinessRules.Provider(minimumRenterAge: minimumRenterAge),
            documents);
    }

    [Fact]
    public async Task It_names_the_zone_every_calendar_answer_is_given_in()
    {
        var config = (await Handler().Handle(new GetAppConfigQuery(), CancellationToken.None)).Value;

        // The date pickers run BEFORE the first quote, so without this the app would have to guess
        // the zone -- and a rental priced in the wrong one is a different number of days.
        Assert.Equal("Asia/Amman", config.TimeZone);
    }

    [Fact]
    public async Task It_says_how_many_decimals_money_is_written_with()
    {
        var config = (await Handler().Handle(new GetAppConfigQuery(), CancellationToken.None)).Value;

        Assert.Equal("JOD", config.Currency.Code);
        // Three, not the two every developer's instinct supplies. A dinar is a thousand fils.
        Assert.Equal(3, config.Currency.MinorUnits);
        Assert.Equal(Money.MinorUnits, config.Currency.MinorUnits);
    }

    [Fact]
    public async Task It_names_how_far_ahead_a_rental_may_be_booked()
    {
        var config = (await Handler().Handle(new GetAppConfigQuery(), CancellationToken.None)).Value;

        // A date picker has to stop somewhere. This is the owner's number, not a bound invented in
        // the app -- and a real trade-off, because a booking freezes the price it was made under.
        Assert.Equal(180, config.MaxAdvanceBookingDays);
    }

    [Fact]
    public async Task It_says_how_long_a_customer_has_to_pay_after_approval()
    {
        var config = (await Handler().Handle(new GetAppConfigQuery(), CancellationToken.None)).Value;

        // The app tells the customer this on the screen where they are waiting for it, so the figure
        // has to be the platform's. Twenty-four hours until push notifications exist to justify less.
        Assert.Equal(24, config.PaymentWindowHours);
    }

    [Fact]
    public async Task It_publishes_the_upload_limits_instead_of_letting_a_client_discover_them_by_being_refused()
    {
        var config = (await Handler().Handle(new GetAppConfigQuery(), CancellationToken.None)).Value;

        Assert.Equal(8 * 1024 * 1024, config.Documents.MaximumSizeBytes);
        Assert.Contains("image/jpeg", config.Documents.AllowedContentTypes);
        // No duplicates. The options binder APPENDS to a collection that already has items, so a
        // property initialiser plus a configured list produced every type twice -- invisible while
        // the only use was a Contains check, and visible the moment a client was handed the list.
        Assert.Equal(
            config.Documents.AllowedContentTypes.Distinct().Count(),
            config.Documents.AllowedContentTypes.Count);
        // A phone camera produces HEIC files near this size; knowing the ceiling up front is the
        // difference between converting before upload and a refusal the customer cannot act on.
        Assert.NotEmpty(config.Documents.AllowedContentTypes);
    }

    [Fact]
    public async Task The_minimum_age_travels_as_a_number_not_as_an_English_sentence()
    {
        var config = (await Handler().Handle(new GetAppConfigQuery(), CancellationToken.None)).Value;

        // Otherwise the only way to learn it is to be refused, in prose the app cannot translate.
        Assert.Equal(21, config.MinimumRenterAge);
    }

    [Fact]
    public async Task No_age_limit_is_a_real_answer_and_says_so()
    {
        var config = (await Handler(minimumRenterAge: null)
            .Handle(new GetAppConfigQuery(), CancellationToken.None)).Value;

        // Null means nobody is refused on age. Zero would read as "everyone is refused".
        Assert.Null(config.MinimumRenterAge);
    }

    [Fact]
    public async Task Every_choice_a_customer_reads_comes_with_both_languages()
    {
        var config = (await Handler().Handle(new GetAppConfigQuery(), CancellationToken.None)).Value;

        var everything = config.Vocabularies.Transmissions
            .Concat(config.Vocabularies.FuelTypes)
            .Concat(config.Vocabularies.PickupMethods)
            .ToList();

        Assert.NotEmpty(everything);
        foreach (var entry in everything)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            Assert.False(string.IsNullOrWhiteSpace(entry.LabelEn));
            Assert.False(string.IsNullOrWhiteSpace(entry.LabelAr));
        }

        // Arabic that is actually Arabic, not the English name repeated.
        var automatic = config.Vocabularies.Transmissions.Single(entry => entry.Name == "Automatic");
        Assert.NotEqual(automatic.LabelEn, automatic.LabelAr);
    }

    [Fact]
    public async Task The_vocabularies_are_the_domain_s_own_members()
    {
        var config = (await Handler().Handle(new GetAppConfigQuery(), CancellationToken.None)).Value;

        // If a fuel type is added to the domain it appears here without anyone remembering to add
        // it -- which is the whole reason a filter chip is not a literal in the app.
        Assert.Equal(
            Enumeration.GetAll<FuelType>().Select(fuel => fuel.Name).OrderBy(name => name),
            config.Vocabularies.FuelTypes.Select(entry => entry.Name).OrderBy(name => name));
    }
}
