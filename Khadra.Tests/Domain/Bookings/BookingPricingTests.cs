using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Fleet;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Bookings;

public sealed class BookingPricingTests
{
    // A fixed local pickup date keeps every money assertion independent of when the suite runs.
    private static readonly DateOnly Pickup = new(2026, 9, 10);

    private static BookingPricing Price(
        decimal dailyRate = 30m,
        int days = 3,
        decimal deliveryFee = 0m,
        decimal depositPercent = 20m) =>
        BookingPricing.Calculate(
            Money.Jod(dailyRate),
            Pickup,
            Pickup.AddDays(days),
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
    public void A_rental_is_billed_by_the_calendar_not_by_the_clock()
    {
        // The owner's own example, settled 2026-09-07: Monday to Thursday is three days. The rule
        // this replaced rounded elapsed time up and charged four whenever the car came back later
        // in the day than it went out.
        Assert.Equal(3, RentalDays.Between(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 10)));
    }

    [Fact]
    public void The_return_time_of_day_cannot_change_the_price()
    {
        var pickedUp = new DateTimeOffset(2026, 9, 7, 6, 0, 0, TimeSpan.Zero);
        // Two returns on the same Amman date: one just after local midnight, one just before it ends.
        var justAfterMidnight = new DateTimeOffset(2026, 9, 9, 21, 1, 0, TimeSpan.Zero);
        var almostMidnight = new DateTimeOffset(2026, 9, 10, 20, 58, 0, TimeSpan.Zero);

        var early = RentalDays.Between(Build.AmmanDate(pickedUp), Build.AmmanDate(justAfterMidnight));
        var late = RentalDays.Between(Build.AmmanDate(pickedUp), Build.AmmanDate(almostMidnight));

        // Both are the tenth in Amman, so both are three days at the same price. That is the rule
        // working as decided, and it is also why a late return is currently unpriced -- logged in
        // docs/pre-launch-checklist.md rather than guessed at.
        Assert.Equal(3, early);
        Assert.Equal(early, late);
    }

    [Fact]
    public void A_rental_returned_the_day_it_started_is_still_billed_for_one_day()
    {
        Assert.Equal(1, RentalDays.Between(Pickup, Pickup));

        var pricing = Price(dailyRate: 30m, days: 0);

        Assert.Equal(1, pricing.Days);
        Assert.Equal(Money.Jod(30m), pricing.RentalTotal);
    }

    [Fact]
    public void An_overnight_rental_that_crosses_one_date_boundary_is_one_day()
    {
        // Out at 23:30, back at 00:30. One boundary crossed, so one day -- not the two that counting
        // the distinct dates it touched would give.
        Assert.Equal(1, RentalDays.Between(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 11)));
    }

    [Fact]
    public void The_dates_the_customer_agreed_to_are_frozen_alongside_the_count()
    {
        var pricing = Price(days: 4);

        // Not redundant with Days: the count is a fact about the Amman calendar, and the zone that
        // produced it is configuration. Freezing the dates means changing that setting can never
        // re-judge a contract that has already been agreed.
        Assert.Equal(Pickup, pricing.PickupDate);
        Assert.Equal(Pickup.AddDays(4), pricing.ReturnDate);
        Assert.Equal(4, pricing.Days);
    }

    [Fact]
    public void Every_amount_on_a_booking_must_share_one_currency()
    {
        var pricing = BookingPricing.Calculate(
            Money.Jod(30m), Pickup, Pickup.AddDays(3), Money.Create(10m, "USD"), Build.Percent(20m),
            Money.Jod(200m), MileagePolicy.Unlimited(), FuelPolicy.FullToFull);

        Assert.Equal("booking.currency_mismatch", pricing.Error.Code);
    }

    [Fact]
    public void The_vehicle_terms_the_customer_agreed_to_are_frozen_onto_the_price()
    {
        var mileage = MileagePolicy.Limited(200, Money.Jod(0.25m)).Value;
        var pricing = BookingPricing.Calculate(
            Money.Jod(30m), Pickup, Pickup.AddDays(3), Money.Jod(0m), Build.Percent(20m),
            Money.Jod(150m), mileage, FuelPolicy.SameToSame).Value;

        // A dealer later tightening the mileage cap or raising the damage deposit cannot reach back
        // into a booking that has already been priced and accepted.
        Assert.Equal(mileage, pricing.Mileage);
        Assert.Same(FuelPolicy.SameToSame, pricing.FuelPolicy);
        Assert.Equal(Money.Jod(150m), pricing.SecurityDeposit);
    }
}

public sealed class BookingTermsTests
{
    private static readonly TimeSpan Turnaround = TimeSpan.FromHours(2);

    [Fact]
    public void Commission_can_never_exceed_the_deposit_it_is_collected_from()
    {
        var terms = BookingTerms.Create(
            Build.Percent(20m), Build.Percent(25m),
            TimeSpan.FromHours(1), TimeSpan.FromHours(8), TimeSpan.FromMinutes(20), TimeSpan.FromHours(48), TimeSpan.FromHours(48),
            Build.Percent(100m), Build.Percent(25m), Build.Percent(50m), Turnaround, 1);

        Assert.Equal("booking.commission_exceeds_deposit", terms.Error.Code);
    }

    [Fact]
    public void The_dealer_penalty_range_cannot_be_inverted()
    {
        var terms = BookingTerms.Create(
            Build.Percent(20m), Build.Percent(20m),
            TimeSpan.FromHours(1), TimeSpan.FromHours(8), TimeSpan.FromMinutes(20), TimeSpan.FromHours(48), TimeSpan.FromHours(48),
            Build.Percent(100m), Build.Percent(50m), Build.Percent(25m), Turnaround, 1);

        Assert.Equal("booking.penalty_range_inverted", terms.Error.Code);
    }

    [Fact]
    public void Non_positive_windows_are_rejected()
    {
        var terms = BookingTerms.Create(
            Build.Percent(20m), Build.Percent(20m),
            TimeSpan.FromHours(1), TimeSpan.Zero, TimeSpan.FromMinutes(20), TimeSpan.FromHours(48), TimeSpan.FromHours(48),
            Build.Percent(100m), Build.Percent(25m), Build.Percent(50m), Turnaround, 1);

        Assert.Equal("booking.invalid_terms", terms.Error.Code);
    }

    [Fact]
    public void A_negative_turnaround_gap_is_not_a_gap()
    {
        var terms = BookingTerms.Create(
            Build.Percent(20m), Build.Percent(20m),
            TimeSpan.FromHours(1), TimeSpan.FromHours(8), TimeSpan.FromMinutes(20), TimeSpan.FromHours(48), TimeSpan.FromHours(48),
            Build.Percent(100m), Build.Percent(25m), Build.Percent(50m), TimeSpan.FromMinutes(-1), 1);

        Assert.Equal("booking.invalid_terms", terms.Error.Code);
    }

    [Fact]
    public void Zero_turnaround_is_a_decision_not_a_missing_value()
    {
        // A gallery that can turn a car around at the counter is allowed to say so. It is ABSENCE
        // that must be refused, and that refusal lives in configuration validation, not here.
        var terms = BookingTerms.Create(
            Build.Percent(20m), Build.Percent(20m),
            TimeSpan.FromHours(1), TimeSpan.FromHours(8), TimeSpan.FromMinutes(20), TimeSpan.FromHours(48), TimeSpan.FromHours(48),
            Build.Percent(100m), Build.Percent(25m), Build.Percent(50m), TimeSpan.Zero, 1);

        Assert.True(terms.IsSuccess);
        Assert.Equal(TimeSpan.Zero, terms.Value.TurnaroundBuffer);
    }

    [Fact]
    public void The_owners_confirmed_numbers_are_a_valid_set_of_terms()
    {
        var terms = Build.Terms();

        Assert.Equal(20m, terms.DepositPercent.Value);
        Assert.Equal(20m, terms.CommissionPercent.Value);
        Assert.Equal(TimeSpan.FromHours(8), terms.NoShowTimeout);
        Assert.Equal(TimeSpan.FromHours(1), terms.FreeCancellationWindow);
        Assert.Equal(TimeSpan.FromHours(2), terms.TurnaroundBuffer);
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

/// <summary>
/// The four names in <c>BookingStatus.HoldsVehicle</c> are written out in four places that cannot
/// share code: the domain property itself, the application's hold predicate, the customer
/// catalogue's availability anti-join, and a WHERE clause inside a raw-SQL migration.
/// </summary>
/// <remarks>
/// The migration's copy is the dangerous one. It compares against the PERSISTED enumeration names,
/// so renaming a status here would leave an exclusion constraint silently guarding nothing — two
/// customers in one car, and no error anywhere. Checklist item 19 asks for exactly this kind of pin.
///
/// If this test fails, the rename is not the bug. The bug is every other spelling that did not move
/// with it, and one of them is a database migration.
/// </remarks>
public sealed class VehicleHoldStatusTests
{
    [Fact]
    public void The_statuses_that_hold_a_car_are_pinned_because_a_migration_spells_them_out()
    {
        var holding = Enumeration.GetAll<BookingStatus>()
            .Where(status => status.HoldsVehicle)
            .Select(status => status.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Renaming any of these is a data migration of bookings_one_hold_per_vehicle, not a rename.
        Assert.Equal(new[] { "Approved", "Confirmed", "PickedUp", "Requested" }, holding);
    }

    [Fact]
    public void Every_status_that_does_not_hold_a_car_is_one_a_customer_has_stopped_waiting_for()
    {
        foreach (var status in Enumeration.GetAll<BookingStatus>().Where(status => !status.HoldsVehicle))
        {
            // Returned is the one non-terminal state that releases the car: it is physically back.
            Assert.True(
                status.IsTerminal || status == BookingStatus.Returned,
                $"{status.Name} releases the vehicle but is neither terminal nor Returned.");
        }
    }
}
