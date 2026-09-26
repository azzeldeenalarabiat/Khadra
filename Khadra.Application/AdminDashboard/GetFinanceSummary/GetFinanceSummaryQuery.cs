using CSharpFunctionalExtensions;
using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Payments.ReadModels;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.AdminDashboard.GetFinanceSummary;

/// <summary>
/// The dashboard's "Money in motion" panel (payments Phase 4b): this Amman month's payments applied and
/// refunds settled, and the refunds still owed right now. Its own request, like every panel.
/// </summary>
public sealed record GetFinanceSummaryQuery : IQuery<Result<FinanceSummaryDto, Error>>;

public sealed class GetFinanceSummaryHandler(
    IPaymentDashboardReader payments,
    IPaymentProvider provider,
    IReportingCalendar calendar,
    IClock clock)
    : IRequestHandler<GetFinanceSummaryQuery, Result<FinanceSummaryDto, Error>>
{
    public async Task<Result<FinanceSummaryDto, Error>> Handle(GetFinanceSummaryQuery request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // The month an administrator in Amman is looking at, as a half-open range of instants.
        var today = calendar.Today(now);
        var first = new DateOnly(today.Year, today.Month, 1);
        var next = first.AddMonths(1);
        var facts = await payments.FinanceAsync(calendar.StartOfDay(first), calendar.StartOfDay(next), cancellationToken);

        // The mode is the adapter's own answer, the one /app-config publishes: never a provider name compared here.
        return FinanceSummaryBuilder.Build(facts, Money.JordanianDinar, provider.Mode.ToString(), first, next, now);
    }
}
