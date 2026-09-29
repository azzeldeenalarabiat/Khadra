using System.Globalization;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Payables;

namespace Khadra.Application.Payables;

/// <summary>
/// Whether a recorded payable still says what the booking's records say (payments Phase 8): the check the payables
/// pass repeats on every open payable, and a settlement repeats on each one it closes, so money never moves on a
/// payable its records have since contradicted.
/// </summary>
public static class PayableComparison
{
    /// <summary>
    /// What differs, in a few words for an administrator — or null when the payable and the records agree on the
    /// outcome, every line in order, and so every figure.
    /// </summary>
    public static string? Difference(OfficePayable recorded, BookingFinancials today)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(today);

        if (today.NeedsReview)
            return $"the records now contradict one another: {string.Join(", ", today.Issues)}";

        var office = today.Office;
        if (!office.IsFinal)
            return $"the booking's outcome is no longer final ({office.State})";
        if (office.Outcome != recorded.Outcome)
            return $"the outcome is now {office.Outcome?.Name}, recorded {recorded.Outcome.Name}";
        if (!string.Equals(office.OfficeMoney.CurrencyCode, recorded.Currency, StringComparison.Ordinal))
            return $"the currency is now {office.OfficeMoney.CurrencyCode}, recorded {recorded.Currency}";
        if (office.Net != recorded.Net)
            return string.Create(CultureInfo.InvariantCulture, $"the net is now {office.Net}, recorded {recorded.Net}");

        var lines = recorded.Lines.ToList();
        if (lines.Count != office.Lines.Count)
            return string.Create(CultureInfo.InvariantCulture, $"the booking now has {office.Lines.Count} lines, recorded {lines.Count}");

        for (var index = 0; index < lines.Count; index++)
        {
            var (was, now) = (lines[index], office.Lines[index]);
            if (was.Kind != now.Kind || was.Amount != now.Amount || was.SourceId != now.SourceId)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"line {index + 1} is now {now.Kind.Name} {now.Amount}, recorded {was.Kind.Name} {was.Amount}");
            }
        }

        return null;
    }
}
