using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Payments.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;

namespace Khadra.Application.AdminDashboard;

/// <summary>
/// Adds up "Money in motion" from the facts the reader returns (payments Phase 4b). Pure, so every
/// definition on the panel is pinned by a unit test rather than spread across SQL and a template.
/// </summary>
/// <remarks>
/// Every figure is in the platform's currency. Money in another currency — only ever a capture the
/// provider took in the wrong one — is listed apart and never added to a figure it cannot be part of.
/// </remarks>
public static class FinanceSummaryBuilder
{
    public static FinanceSummaryDto Build(
        FinanceFacts facts,
        string currency,
        string paymentMode,
        DateOnly monthFrom,
        DateOnly monthTo,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        bool Ours(string code) => string.Equals(code, currency, StringComparison.Ordinal);
        MoneyDto Total(IEnumerable<decimal> amounts) => MoneyDto.From(Money.Create(amounts.Sum(), currency));

        var applied = facts.AppliedThisMonth.Where(payment => Ours(payment.Currency)).ToList();
        var settled = facts.SettledThisMonth.Where(refund => Ours(refund.Currency)).ToList();
        var outstanding = facts.Outstanding.Where(refund => Ours(refund.Currency)).ToList();
        var failed = outstanding.Where(refund => refund.Status == RefundStatus.Failed.Name).ToList();
        var inProgress = outstanding.Where(refund => refund.Status != RefundStatus.Failed.Name).ToList();
        var orphans = outstanding.Where(refund => refund.Reason == RefundReason.OrphanedCapture.Name).ToList();

        var others = facts.SettledThisMonth
            .Concat(facts.Outstanding)
            .Select(refund => refund.Currency)
            .Where(code => !Ours(code))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .Select(code => new FinanceOtherCurrencyDto(
                code,
                MoneyDto.From(Money.Create(facts.SettledThisMonth.Where(refund => refund.Currency == code).Sum(refund => refund.Amount), code)),
                MoneyDto.From(Money.Create(facts.Outstanding.Where(refund => refund.Currency == code).Sum(refund => refund.Amount), code))))
            .ToList();

        return new FinanceSummaryDto(
            now,
            paymentMode,
            currency,
            new FinanceThisMonthDto(
                monthFrom,
                monthTo,
                Total(applied.Select(payment => payment.AppliedToBooking)),
                applied.Count,
                Total(applied.Select(payment => payment.ProcessingFee)),
                Total(settled.Select(refund => refund.Amount)),
                settled.Count),
            new FinanceRightNowDto(
                Total(inProgress.Select(refund => refund.Amount)),
                inProgress.Count,
                Total(failed.Select(refund => refund.Amount)),
                failed.Count,
                Total(orphans.Select(refund => refund.Amount)),
                orphans.Count),
            others);
    }
}
