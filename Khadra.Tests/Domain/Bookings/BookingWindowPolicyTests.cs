using CSharpFunctionalExtensions;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Bookings;

/// <summary>
/// The two bounds on when a rental may start, and the boundary instants of each.
/// </summary>
/// <remarks>
/// Boundaries are the whole substance of this file. A policy that refuses at exactly the lead time,
/// or accepts one day past the horizon, is wrong in a way no round-number test would catch, and
/// three surfaces — search, quote and create — give the same answer only because they share this.
/// </remarks>
public sealed class BookingWindowPolicyTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly TimeSpan LeadTime = TimeSpan.FromHours(2);
    private const int HorizonDays = 180;
    private const int MaxRentalDays = 90;

    // A three-day rental unless a test is about the length, so every other test is about its own
    // bound and nothing else.
    private static UnitResult<Error> Validate(
        DateTimeOffset start,
        DateTimeOffset? now = null,
        int billedDays = 3) =>
        BookingWindowPolicy.Validate(
            DateRange.Create(start, start.AddDays(3)).Value,
            now ?? Now,
            LeadTime,
            HorizonDays,
            billedDays,
            MaxRentalDays);

    [Fact]
    public void A_rental_cannot_start_in_the_past()
    {
        Assert.Equal("booking.period_in_past", Validate(Now.AddDays(-1)).Error.Code);
    }

    [Fact]
    public void A_rental_cannot_start_at_this_very_instant()
    {
        // The past check is `<=`, not `<`. "Now" is already gone by the time anything acts on it.
        Assert.Equal("booking.period_in_past", Validate(Now).Error.Code);
    }

    [Fact]
    public void A_rental_inside_the_lead_time_is_refused()
    {
        Assert.Equal("booking.too_soon", Validate(Now.AddMinutes(119)).Error.Code);
    }

    [Fact]
    public void A_rental_exactly_at_the_lead_time_is_allowed()
    {
        // The bound is inclusive: two hours' notice IS two hours' notice.
        Assert.True(Validate(Now.Add(LeadTime)).IsSuccess);
    }

    [Fact]
    public void The_refusal_carries_the_figure_the_customer_needs()
    {
        // A phone must never restate a business number of its own beside the server's, so the
        // message has to carry it.
        Assert.Contains("2 hours", Validate(Now.AddMinutes(30)).Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lead_time_under_an_hour_is_stated_in_minutes()
    {
        var result = BookingWindowPolicy.Validate(
            DateRange.Create(Now.AddMinutes(5), Now.AddDays(3)).Value,
            Now,
            TimeSpan.FromMinutes(30),
            HorizonDays,
            billedDays: 3,
            MaxRentalDays);

        Assert.Contains("30 minutes", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rental_beyond_the_horizon_is_refused()
    {
        var result = Validate(Now.AddDays(HorizonDays).AddMinutes(1));

        Assert.Equal("booking.beyond_horizon", result.Error.Code);
        Assert.Contains("180 days", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rental_exactly_at_the_horizon_is_allowed()
    {
        Assert.True(Validate(Now.AddDays(HorizonDays)).IsSuccess);
    }

    [Fact]
    public void An_ordinary_rental_a_week_out_passes()
    {
        Assert.True(Validate(Now.AddDays(7)).IsSuccess);
    }

    /// <summary>
    /// The past check comes first. A date in the past is also inside the lead time, and being told
    /// "book at least two hours ahead" about yesterday is not an answer.
    /// </summary>
    [Fact]
    public void A_past_date_is_reported_as_past_rather_than_as_too_soon()
    {
        Assert.Equal("booking.period_in_past", Validate(Now.AddHours(-1)).Error.Code);
    }

    // ── How long it may run ───────────────────────────────────────────────────────────────────

    [Fact]
    public void A_rental_longer_than_the_maximum_is_refused()
    {
        var result = Validate(Now.AddDays(7), billedDays: 91);

        Assert.Equal("booking.rental_too_long", result.Error.Code);
        // Both figures, because a customer cannot act on "too long" alone.
        Assert.Contains("90", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("91", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rental_exactly_at_the_maximum_is_allowed()
    {
        Assert.True(Validate(Now.AddDays(7), billedDays: 90).IsSuccess);
    }

    /// <summary>
    /// The length is judged on BILLED days, which the caller counts through IReportingCalendar. The
    /// policy has no clock and no calendar, so it cannot second-guess that number — and must not:
    /// counting elapsed hours here would refuse a rental the price says is shorter.
    /// </summary>
    [Fact]
    public void The_length_is_the_callers_billed_count_not_the_periods_own_span()
    {
        // A period spanning three days, declared as one billed day. The policy judges what it is
        // given, which is what keeps the refusal and the quote in agreement.
        Assert.True(Validate(Now.AddDays(7), billedDays: 1).IsSuccess);
    }

    /// <summary>
    /// Start bounds come first: a rental that is both too soon AND too long is refused for the
    /// reason the customer can act on by moving one date rather than two.
    /// </summary>
    [Fact]
    public void A_rental_that_breaks_two_bounds_is_reported_on_the_start()
    {
        Assert.Equal("booking.too_soon", Validate(Now.AddMinutes(10), billedDays: 500).Error.Code);
    }
}
