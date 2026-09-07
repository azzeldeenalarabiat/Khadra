using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings;

/// <summary>
/// What cancelling this booking would cost, asked before anyone commits to it.
/// </summary>
/// <remarks>
/// It exists so a screen can put a figure on a confirmation sheet without working one out. The
/// alternative — a client reading <c>Terms.CustomerCancellationPenaltyPercent</c> and multiplying it
/// by <c>Pricing.DepositAmount</c> — is the same mistake as recomputing the day count: two places
/// arriving at one number, and the screen is the one that ends up wrong. It is also how a button
/// knows whether to be enabled at all, rather than finding out from a refused request.
///
/// The penalty is an ASSESSMENT and nothing is charged without a dispute ticket (spec 3.3), which is
/// why the assessment travels whole rather than as a bare amount: whatever renders it can read
/// <see cref="PenaltyAssessment.RequiresTicketToEnforce"/> instead of being told the rule in prose.
/// </remarks>
public sealed record CancellationPreview(
    bool CanCancel,
    /// True when cancelling right now costs the party nothing. Distinct from a zero amount only in
    /// what it means: a booking that cannot be cancelled at all also has nothing to pay.
    bool IsFree,
    PenaltyAssessment Penalty)
{
    public static CancellationPreview Allowed(PenaltyAssessment penalty)
    {
        ArgumentNullException.ThrowIfNull(penalty);
        return new CancellationPreview(true, penalty.IsNothingOwed, penalty);
    }

    /// <summary>
    /// The booking is past cancelling: it already ended, or a window closed on it and only the row
    /// has yet to catch up.
    /// </summary>
    public static CancellationPreview NotAllowed(string currencyCode, DateTimeOffset now) =>
        new(
            false,
            true,
            PenaltyAssessment.None("This booking can no longer be cancelled.", currencyCode, now));
}
