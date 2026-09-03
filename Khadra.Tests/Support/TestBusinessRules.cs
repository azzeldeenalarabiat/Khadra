using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using NSubstitute;

namespace Khadra.Tests.Support;

// The configured business numbers a handler test needs, with the confirmed values from appsettings.
// Tests that care about a specific number override just that one.
internal static class TestBusinessRules
{
    public const int MinimumRenterAge = 21;

    public static BusinessRules Values(int? minimumRenterAge = MinimumRenterAge) => new(
        CommissionPercent: 20m,
        DepositPercent: 20m,
        NoShowTimeoutHours: 8,
        DeliveryFee: Money.Jod(10m),
        DealerNonDeliveryPenaltyMinPercent: 25m,
        DealerNonDeliveryPenaltyMaxPercent: 50m,
        FreeCancellationWindowMinutes: 60,
        AdminSlaHours: 48,
        CustomerCancellationPenaltyPercent: 100m,
        PaymentWindowMinutes: 20,
        PostReturnSettlementHours: 48,
        MinimumRenterAge: minimumRenterAge);

    public static IBusinessRulesProvider Provider(int? minimumRenterAge = MinimumRenterAge)
    {
        var provider = Substitute.For<IBusinessRulesProvider>();
        provider.GetAsync(Arg.Any<CancellationToken>()).Returns(Values(minimumRenterAge));
        return provider;
    }

    // Amman is UTC+3 with no daylight saving, which is all the age check needs from a calendar.
    public static IReportingCalendar Calendar()
    {
        var calendar = Substitute.For<IReportingCalendar>();
        calendar.Today(Arg.Any<DateTimeOffset>())
            .Returns(call => DateOnly.FromDateTime(call.Arg<DateTimeOffset>().ToOffset(TimeSpan.FromHours(3)).DateTime));
        calendar.DayOf(Arg.Any<DateTimeOffset>())
            .Returns(call => DateOnly.FromDateTime(call.Arg<DateTimeOffset>().ToOffset(TimeSpan.FromHours(3)).DateTime));
        // Local midnight in Amman, expressed as the UTC instant it is.
        calendar.StartOfDay(Arg.Any<DateOnly>())
            .Returns(call => new DateTimeOffset(call.Arg<DateOnly>().ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(3)).ToUniversalTime());
        return calendar;
    }
}
