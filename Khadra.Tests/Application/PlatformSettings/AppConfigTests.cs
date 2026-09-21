using Khadra.Application.Common.Ports;
using Khadra.Application.PlatformSettings.AppConfig;
using Khadra.Domain.Common;
using Khadra.Domain.Fleet;
using Khadra.Domain.IdentityAccess;
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
    private static GetAppConfigHandler Handler(
        int? minimumRenterAge = 21,
        int passwordMinimumLength = 8,
        PaymentMode paymentMode = PaymentMode.None)
    {
        var payments = Substitute.For<IPaymentProvider>();
        payments.Mode.Returns(paymentMode);

        var calendar = Substitute.For<IReportingCalendar>();
        calendar.TimeZoneId.Returns("Asia/Amman");

        var documents = Substitute.For<IDocumentPolicySettings>();
        documents.MaximumSizeBytes.Returns(8L * 1024 * 1024);
        documents.AllowedContentTypes.Returns(["image/jpeg", "image/png", "application/pdf"]);

        var authPolicy = Substitute.For<IAuthPolicySettings>();
        authPolicy.PasswordMinimumLength.Returns(passwordMinimumLength);

        return new GetAppConfigHandler(
            calendar,
            TestBusinessRules.Provider(minimumRenterAge: minimumRenterAge),
            documents,
            authPolicy,
            payments);
    }

    /// <summary>
    /// What kind of money this deployment moves, in one word, before any screen shows a price.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It cannot be worked out from anything else a client is told: the booking says nothing about
    /// it, and the payment availability verdict answers "can I pay", not "is it real". The value is
    /// what the sandbox banner keys on, and the banner is what keeps a test booking from looking like
    /// a paid one to whoever opens the app next.
    /// </para>
    /// <para>
    /// Taken straight from the adapter's own <c>Mode</c>, so a future provider reports itself rather
    /// than being recognised here by name.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(PaymentMode.None, "None")]
    [InlineData(PaymentMode.Sandbox, "Sandbox")]
    [InlineData(PaymentMode.Live, "Live")]
    public async Task It_says_what_kind_of_money_this_deployment_moves(PaymentMode mode, string published)
    {
        var config = (await Handler(paymentMode: mode)
            .Handle(new GetAppConfigQuery(), CancellationToken.None)).Value;

        Assert.Equal(published, config.Payments.Mode);
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
        // has to be the platform's. TWO hours since 2026-09-11, the owner's decision — and not to be
        // confused with MinimumBookingLeadTimeMinutes, which is also 120 and is a different clock.
        Assert.Equal(2, config.PaymentWindowHours);

        // The lead time, published on the same document. Four hours since 2026-09-11, and the two
        // are no longer independent: a gallery may not approve unless the customer can still have
        // the whole payment window, so the GAP between them is the time a gallery has to answer a
        // request made at the earliest a customer may book for.
        Assert.Equal(240, config.MinimumBookingLeadTimeMinutes);
        Assert.True(
            TimeSpan.FromMinutes(config.MinimumBookingLeadTimeMinutes)
                > TimeSpan.FromHours(config.PaymentWindowHours),
            "A lead time no longer than the payment window makes every booking at the minimum lead "
            + "time impossible to approve.");
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

    [Fact]
    public async Task It_publishes_what_makes_a_password_acceptable()
    {
        var config = (await Handler(passwordMinimumLength: 12)
            .Handle(new GetAppConfigQuery(), CancellationToken.None)).Value;

        var password = config.Password;

        // The CONFIGURED figure, not the shipped default. Without this the app goes on promising 8
        // the day the owner raises it, and accepts a 9-character password the server then refuses.
        Assert.Equal(12, password.MinimumLength);
        Assert.Equal(PasswordPolicy.MaximumLength, password.MaximumLength);
        Assert.True(password.RequiresLetter);
        Assert.True(password.RequiresDigit);
        Assert.False(password.AllowsWhitespace);
    }

    [Fact]
    public async Task The_published_minimum_is_never_below_the_one_enforced()
    {
        // A configured minimum below the absolute floor is possible -- `AuthOptions` allows 8..64,
        // and nothing stops a future default or a bad environment value going lower. Publishing the
        // raw figure would then PROMISE a password the validator refuses, which is worse than
        // publishing nothing: the app would clear it locally and the server would still say no.
        var config = (await Handler(passwordMinimumLength: 4)
            .Handle(new GetAppConfigQuery(), CancellationToken.None)).Value;

        Assert.Equal(PasswordPolicy.AbsoluteMinimumLength, config.Password.MinimumLength);
        Assert.Equal(
            PasswordPolicy.EffectiveMinimum(4),
            config.Password.MinimumLength);

        // And the same expression really is the one that judges.
        var refused = PasswordPolicy.Validate(new string('a', 4) + "1", 4);
        Assert.True(refused.IsFailure);
    }
}
