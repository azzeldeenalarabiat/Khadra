using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Fleet;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Bookings;

public sealed class BookingPricingTests
{
    private static BookingPricing Price(
        decimal dailyRate = 30m,
        int days = 3,
        decimal deliveryFee = 0m,
        decimal depositPercent = 20m) =>
        BookingPricing.Calculate(
            Money.Jod(dailyRate),
            days,
            Money.Jod(deliveryFee),
            Build.Percent(depositPercent),
            Money.Jod(200m),
            MileagePolicy.Unlimited(),
            FuelPolicy.FullToFull).Value;

    [Fact]
    public void The_deposit_is_a_share_of_the_rental_and_the_rest_is_cash_to_the_dealer()
    {
        var pricing = Price(dailyRate: 30m, days: 3);

        Assert.Equal(Money.Jod(90m), pricing.RentalTotal);
        Assert.Equal(Money.Jod(90m), pricing.TotalPrice);
        Assert.Equal(Money.Jod(18m), pricing.DepositAmount);
        Assert.Equal(Money.Jod(72m), pricing.BalanceDue);
    }

    [Fact]
    public void The_delivery_fee_is_added_to_the_total_but_never_to_the_deposit_base()
    {
        // The 10 JOD covers the driver's trip; it is not rental revenue to take a deposit against.
        var pricing = Price(dailyRate: 30m, days: 3, deliveryFee: 10m);

        Assert.Equal(Money.Jod(90m), pricing.RentalTotal);
        Assert.Equal(Money.Jod(100m), pricing.TotalPrice);
        Assert.Equal(Money.Jod(18m), pricing.DepositAmount);
        // The customer hands over the unpaid rental plus the delivery fee in cash on arrival.
        Assert.Equal(Money.Jod(82m), pricing.BalanceDue);
    }

    [Fact]
    public void Deposit_and_balance_always_add_back_up_to_the_total()
    {
        foreach (var days in new[] { 1, 3, 7, 30 })
        {
            var pricing = Price(dailyRate: 27.5m, days: days, deliveryFee: 10m);

            Assert.Equal(pricing.TotalPrice, pricing.DepositAmount.Add(pricing.BalanceDue));
        }
    }

    [Fact]
    public void A_booking_must_cover_at_least_one_day()
    {
        var pricing = BookingPricing.Calculate(
            Money.Jod(30m), 0, Money.Jod(0m), Build.Percent(20m),
            Money.Jod(200m), MileagePolicy.Unlimited(), FuelPolicy.FullToFull);

        Assert.Equal("booking.period_too_short", pricing.Error.Code);
    }

    [Fact]
    public void Every_amount_on_a_booking_must_share_one_currency()
    {
        var pricing = BookingPricing.Calculate(
            Money.Jod(30m), 3, Money.Create(10m, "USD"), Build.Percent(20m),
            Money.Jod(200m), MileagePolicy.Unlimited(), FuelPolicy.FullToFull);

        Assert.Equal("booking.currency_mismatch", pricing.Error.Code);
    }

    [Fact]
    public void The_vehicle_terms_the_customer_agreed_to_are_frozen_onto_the_price()
    {
        var mileage = MileagePolicy.Limited(200, Money.Jod(0.25m)).Value;
        var pricing = BookingPricing.Calculate(
            Money.Jod(30m), 3, Money.Jod(0m), Build.Percent(20m),
            Money.Jod(150m), mileage, FuelPolicy.SameToSame).Value;

        // A dealer later tightening the mileage cap or raising the damage deposit cannot reach back
        // into a booking that has already been priced and accepted.
        Assert.Equal(mileage, pricing.Mileage);
        Assert.Same(FuelPolicy.SameToSame, pricing.FuelPolicy);
        Assert.Equal(Money.Jod(150m), pricing.SecurityDeposit);
    }

    [Fact]
    public void A_full_day_is_charged_for_any_part_of_a_day()
    {
        var period = DateRange.Create(Build.Now, Build.Now.AddDays(2).AddHours(3)).Value;

        Assert.Equal(3, period.WholeDays);
    }
}

public sealed class BookingTermsTests
{
    [Fact]
    public void Commission_can_never_exceed_the_deposit_it_is_collected_from()
    {
        var terms = BookingTerms.Create(
            Build.Percent(20m), Build.Percent(25m),
            TimeSpan.FromHours(1), TimeSpan.FromHours(8), TimeSpan.FromMinutes(20), TimeSpan.FromHours(48),
            Build.Percent(100m), Build.Percent(25m), Build.Percent(50m), 1);

        Assert.Equal("booking.commission_exceeds_deposit", terms.Error.Code);
    }

    [Fact]
    public void The_dealer_penalty_range_cannot_be_inverted()
    {
        var terms = BookingTerms.Create(
            Build.Percent(20m), Build.Percent(20m),
            TimeSpan.FromHours(1), TimeSpan.FromHours(8), TimeSpan.FromMinutes(20), TimeSpan.FromHours(48),
            Build.Percent(100m), Build.Percent(50m), Build.Percent(25m), 1);

        Assert.Equal("booking.penalty_range_inverted", terms.Error.Code);
    }

    [Fact]
    public void Non_positive_windows_are_rejected()
    {
        var terms = BookingTerms.Create(
            Build.Percent(20m), Build.Percent(20m),
            TimeSpan.FromHours(1), TimeSpan.Zero, TimeSpan.FromMinutes(20), TimeSpan.FromHours(48),
            Build.Percent(100m), Build.Percent(25m), Build.Percent(50m), 1);

        Assert.Equal("booking.invalid_terms", terms.Error.Code);
    }

    [Fact]
    public void The_owners_confirmed_numbers_are_a_valid_set_of_terms()
    {
        var terms = Build.Terms();

        Assert.Equal(20m, terms.DepositPercent.Value);
        Assert.Equal(20m, terms.CommissionPercent.Value);
        Assert.Equal(TimeSpan.FromHours(8), terms.NoShowTimeout);
        Assert.Equal(TimeSpan.FromHours(1), terms.FreeCancellationWindow);
        Assert.Equal(1, terms.RulesVersion);
    }
}

public sealed class PenaltyAssessmentTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void Nothing_owed_is_attributed_to_nobody()
    {
        var assessment = PenaltyAssessment.None("free cancellation", "JOD", Now);

        Assert.True(assessment.IsNothingOwed);
        Assert.Same(BookingParty.Unattributed, assessment.AttributedTo);
        Assert.False(assessment.IsRange);
    }

    [Fact]
    public void A_fixed_assessment_reports_one_figure()
    {
        var assessment = PenaltyAssessment.Fixed(
            BookingParty.Customer, Build.Percent(100m), Money.Jod(18m), "late cancellation", Now);

        Assert.Equal(Money.Jod(18m), assessment.MinAmount);
        Assert.Equal(Money.Jod(18m), assessment.MaxAmount);
        Assert.False(assessment.IsRange);
    }

    [Fact]
    public void A_range_assessment_leaves_the_exact_figure_to_an_admin()
    {
        var assessment = PenaltyAssessment.Range(
            BookingParty.Dealer, Build.Percent(25m), Build.Percent(50m), Money.Jod(90m), "non-delivery", Now);

        Assert.True(assessment.IsRange);
        Assert.Equal(Money.Jod(22.5m), assessment.MinAmount);
        Assert.Equal(Money.Jod(45m), assessment.MaxAmount);
    }

    [Fact]
    public void An_inverted_range_is_a_programming_error()
    {
        Assert.Throws<DomainException>(() => PenaltyAssessment.Range(
            BookingParty.Dealer, Build.Percent(50m), Build.Percent(25m), Money.Jod(90m), "bad", Now));
    }

    [Fact]
    public void Every_assessment_says_out_loud_that_it_needs_a_ticket()
    {
        Assert.True(PenaltyAssessment.None("x", "JOD", Now).RequiresTicketToEnforce);
        Assert.True(PenaltyAssessment
            .Fixed(BookingParty.Customer, Build.Percent(10m), Money.Jod(90m), "x", Now)
            .RequiresTicketToEnforce);
    }
}
