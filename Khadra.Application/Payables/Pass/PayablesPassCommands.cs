using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.Payables.ReadModels;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payables;
using Khadra.Domain.Payables.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Payables.Pass;

/// <summary>What the payables pass looks at on one tick: bookings to record, and a page of open payables to check.</summary>
/// <param name="VerifyOffset">Where this tick's page of open payables starts: the previous tick's answer, or 0.</param>
public sealed record ListPayableWorkQuery(int VerifyOffset) : IQuery<PayableWork>;

/// <summary>
/// Records ONE final booking's payable, in its own scope and transaction (payments Phase 8) — or holds it back and
/// says why.
/// </summary>
public sealed record RecordOfficePayableCommand(Id BookingId) : ICommand<PayableStep>;

/// <summary>Checks ONE open payable against its booking's records again, and holds it back if they now differ.</summary>
public sealed record VerifyOfficePayableCommand(Id PayableId) : ICommand<PayableStep>;

/// <summary>What one step of the payables pass did, as a stable code the tests and the log read.</summary>
public sealed record PayableStep(string Outcome)
{
    public static readonly PayableStep Recorded = new("recorded");
    public static readonly PayableStep AlreadyRecorded = new("already_recorded");
    public static readonly PayableStep NotFinal = new("not_final");
    public static readonly PayableStep Held = new("held");
    public static readonly PayableStep LostRace = new("lost_race");
    public static readonly PayableStep Verified = new("verified");
    public static readonly PayableStep Contradicted = new("contradicted");
    public static readonly PayableStep Skipped = new("skipped");
    public static readonly PayableStep NotFound = new("not_found");
}

public sealed class ListPayableWorkHandler(
    IPayableWorkReader work,
    IPayablesSettings settings,
    IBusinessRulesProvider businessRules,
    IClock clock)
    : IRequestHandler<ListPayableWorkQuery, PayableWork>
{
    public async Task<PayableWork> Handle(ListPayableWorkQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        var completedBefore = now - settings.FinalityMargin;
        // Today's window only BOUNDS the candidates; each booking's own frozen window decides, in the step.
        var rules = await businessRules.GetAsync(cancellationToken);
        return await work.ListAsync(
            now,
            completedBefore,
            completedBefore - TimeSpan.FromHours(rules.PostReturnSettlementHours),
            settings.MaxBookingsPerPass,
            Math.Max(0, request.VerifyOffset),
            settings.MaxVerificationsPerPass,
            cancellationToken);
    }
}

/// <summary>
/// Records a final booking's payable exactly as the one calculator states it (payments Phase 8).
/// </summary>
/// <remarks>
/// <para>
/// <b>From committed facts only.</b> The booking is read as STORED, as documents are
/// (<see cref="IFinancialDocumentFactsReader"/>): a payable is a record, and a lapse applied in memory would be one
/// nobody saved. And only once the outcome has been final for the margin, so a fact committed just after it with
/// an earlier instant is read too.
/// </para>
/// <para>
/// <b>Never a contradiction.</b> A booking whose records contradict one another is held, not recorded: its payable
/// would freeze the contradiction into an office's balance. So is an outcome that cannot be recorded — a
/// customer's penalty that is not the whole deposit held. Each hold is looked at again later, and released the
/// moment the booking can be recorded.
/// </para>
/// <para>
/// <b>Once.</b> A booking has at most one payable, which the database enforces: a second process recording the
/// same booking loses on the key, and this step says so rather than failing.
/// </para>
/// </remarks>
public sealed partial class RecordOfficePayableHandler(
    IFinancialDocumentFactsReader facts,
    IOfficePayableRepository payables,
    IOfficePayableHoldRepository holds,
    IPayablesSettings settings,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<RecordOfficePayableHandler> logger)
    : IRequestHandler<RecordOfficePayableCommand, PayableStep>
{
    public async Task<PayableStep> Handle(RecordOfficePayableCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;

        if (await payables.GetByBookingAsync(request.BookingId, cancellationToken) is not null)
            return PayableStep.AlreadyRecorded;
        var money = await facts.BookingAsync(request.BookingId, cancellationToken);
        if (money is null)
            return PayableStep.NotFound;

        var booking = money.Booking;
        var financials = BookingFinancialsCalculator.Calculate(booking, money.Payments, money.ResolvedTickets, money.HasLiveDispute, now);
        var open = (await holds.ListOpenForBookingAsync(booking.Id, cancellationToken))
            .Where(hold => hold.Reason.BeforeRecording)
            .ToList();

        if (financials.NeedsReview)
            return await HoldAsync(booking, open, PayableHoldReason.NeedsReview, string.Join(", ", financials.Issues), now, cancellationToken);

        var office = financials.Office;
        if (office.State == OfficeStates.Undetermined)
        {
            var reason = Enumeration.FromName<PayableHoldReason>(office.UndeterminedBecause!);
            return await HoldAsync(booking, open, reason, Undetermined(booking), now, cancellationToken);
        }

        if (!office.IsFinal || now < office.FinalAt!.Value.Add(settings.FinalityMargin))
        {
            // Its records agree and its outcome can be recorded once final: a hold opened for a reason that no longer
            // holds leaves the work queue now, not when the booking turns final.
            if (open.Count == 0)
                return PayableStep.NotFinal;
            foreach (var hold in open)
                hold.ReleaseBySystem(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return PayableStep.NotFinal;
        }

        // The payment that confirmed the booking exists and applied: a booking whose record says otherwise needs
        // review, and was held above.
        var confirming = money.Payments.First(payment => payment.Id == booking.DepositPaymentId);
        var payable = OfficePayable.Record(
            new PayableDraft(
                booking.Id,
                booking.DealerId,
                booking.Reference.Value,
                financials.Currency,
                confirming.Provider,
                office.Outcome!,
                office.FinalAt!.Value,
                BookingFinancials.CalculatorVersion,
                office.Lines),
            now);
        payables.Add(payable);
        foreach (var hold in open)
            hold.ReleaseBySystem(now);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintConflictException)
        {
            return PayableStep.LostRace;
        }

        LogRecorded(logger, booking.Reference.Value, payable.Outcome.Name, payable.Net, payable.Currency);
        return PayableStep.Recorded;
    }

    private async Task<PayableStep> HoldAsync(
        Booking booking,
        List<OfficePayableHold> open,
        PayableHoldReason reason,
        string detail,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = open.Find(hold => hold.Reason == reason);
        var next = now + PayableRetry.Delay(settings, (existing?.Checks ?? 0) + 1);
        if (existing is null)
        {
            holds.Add(OfficePayableHold.OpenBySystem(reason, booking.Id, booking.DealerId, null, detail, now, next));
            LogHeld(logger, booking.Reference.Value, reason.Name, detail);
        }
        else if (existing.NextCheckAt is { } due && due > now)
        {
            // Looked at before its time (a candidate query that let it through): nothing new to record.
            return PayableStep.Held;
        }
        else
        {
            existing.CheckedAgain(detail, now, next);
        }

        // One reason at a time: a booking whose records were put right but whose penalty cannot be kept is held for
        // that, not for both.
        foreach (var other in open.Where(hold => hold.Reason != reason))
            other.ReleaseBySystem(now);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintConflictException)
        {
            return PayableStep.LostRace;
        }

        return PayableStep.Held;
    }

    private static string Undetermined(Booking booking) =>
        booking.Penalty is { } penalty
            ? $"penalty {penalty.MaxAmount.Amount} {penalty.MaxAmount.CurrencyCode}{(penalty.IsRange ? " (a range)" : string.Empty)}, deposit {booking.Pricing.DepositAmount.Amount} {booking.Pricing.CurrencyCode}"
            : "no penalty";

    [LoggerMessage(2800, LogLevel.Information, "Booking {Reference}: its payable was recorded ({Outcome}, net {Net} {Currency}).")]
    private static partial void LogRecorded(ILogger logger, string reference, string outcome, decimal net, string currency);

    [LoggerMessage(
        2801,
        LogLevel.Warning,
        "Booking {Reference}: its payable is held ({Reason}: {Detail}). The office's balance leaves it out until somebody puts it right.")]
    private static partial void LogHeld(ILogger logger, string reference, string reason, string detail);
}

/// <summary>
/// Checks an open payable against its booking's records again (payments Phase 8). A difference holds it back from
/// every settlement — its figures are never changed — and an administrator is shown why; agreement again releases it.
/// </summary>
public sealed partial class VerifyOfficePayableHandler(
    IOfficePayableRepository payables,
    IOfficePayableHoldRepository holds,
    IFinancialDocumentFactsReader facts,
    IPayablesSettings settings,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<VerifyOfficePayableHandler> logger)
    : IRequestHandler<VerifyOfficePayableCommand, PayableStep>
{
    public async Task<PayableStep> Handle(VerifyOfficePayableCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;

        var payable = await payables.GetAsync(request.PayableId, cancellationToken);
        if (payable is null || payable.IsSettled)
            return PayableStep.Skipped;

        var money = await facts.BookingAsync(payable.BookingId, cancellationToken);
        var difference = money is null
            ? "the booking can no longer be read"
            : PayableComparison.Difference(
                payable,
                BookingFinancialsCalculator.Calculate(money.Booking, money.Payments, money.ResolvedTickets, money.HasLiveDispute, now));
        var open = (await holds.ListOpenForBookingAsync(payable.BookingId, cancellationToken))
            .FirstOrDefault(hold => hold.Reason == PayableHoldReason.Contradicted);

        if (difference is null)
        {
            if (open is null)
                return PayableStep.Verified;

            open.ReleaseBySystem(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            LogAgrees(logger, payable.BookingReference);
            return PayableStep.Verified;
        }

        if (open is null)
        {
            holds.Add(OfficePayableHold.OpenBySystem(
                PayableHoldReason.Contradicted,
                payable.BookingId,
                payable.DealerId,
                payable.Id,
                difference,
                now,
                now + PayableRetry.Delay(settings, 1)));
            LogContradicted(logger, payable.BookingReference, difference);
        }
        else if (open.NextCheckAt is not { } due || due <= now)
        {
            open.CheckedAgain(difference, now, now + PayableRetry.Delay(settings, open.Checks + 1));
        }
        else
        {
            return PayableStep.Contradicted;
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintConflictException)
        {
            return PayableStep.LostRace;
        }

        return PayableStep.Contradicted;
    }

    [LoggerMessage(
        2802,
        LogLevel.Error,
        "Booking {Reference}: its recorded payable no longer matches its records ({Difference}). It is held back from every settlement; somebody needs to look.")]
    private static partial void LogContradicted(ILogger logger, string reference, string difference);

    [LoggerMessage(2803, LogLevel.Information, "Booking {Reference}: its recorded payable matches its records again and is no longer held.")]
    private static partial void LogAgrees(ILogger logger, string reference);
}

/// <summary>How long a held booking waits before the pass looks again: doubling from the first delay, up to the longest.</summary>
internal static class PayableRetry
{
    public static TimeSpan Delay(IPayablesSettings settings, int check)
    {
        var factor = Math.Pow(2, Math.Clamp(check - 1, 0, 30));
        var ticks = Math.Min(settings.RetryInitialDelay.Ticks * factor, settings.RetryMaxDelay.Ticks);
        return TimeSpan.FromTicks((long)ticks);
    }
}
