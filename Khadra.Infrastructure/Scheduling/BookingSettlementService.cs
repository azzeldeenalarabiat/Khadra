using Khadra.Application.Bookings.Reminders;
using Khadra.Application.Bookings.SettleBookings;
using Khadra.Application.FinancialDocuments.Issuance;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Application.Payables.Pass;
using Khadra.Application.Payments.SettlePayments;
using Khadra.Infrastructure.Configuration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Scheduling;

/// <summary>
/// The timer that closes pre-launch checklist item 4.
/// </summary>
/// <remarks>
/// Four booking rules were enforced correctly wherever anyone asked and nobody asked on a schedule.
/// This asks. It owns no rules of its own — every decision belongs to the aggregate, and this only
/// decides HOW OFTEN to look.
///
/// A scope per pass, because the handler's dependencies are scoped and a background service is a
/// singleton. Every exception is swallowed and logged: a hosted service that throws out of
/// ExecuteAsync stops for the lifetime of the process, and a settlement pass failing once is not a
/// reason to stop settling bookings until somebody restarts the API.
///
/// Since 2026-09-08 it drives the payment sweep as well, which closes checkout attempts a provider
/// stopped talking about and sends the refunds the platform owes. Same reasoning, same scope, same
/// swallow-and-log: neither job owns a rule, and both only decide how often to look.
///
/// One instance is assumed. Two processes running this concurrently is not a correctness problem —
/// the transitions are idempotent and the concurrency token makes the loser retry — but it is wasted
/// work, and a leader election belongs with the deployment story rather than here. Recorded on the
/// pre-launch checklist.
/// </remarks>
internal sealed partial class BookingSettlementService(
    IServiceScopeFactory scopes,
    IOptions<SchedulingOptions> options,
    IOptions<FinancialDocumentOptions> documentOptions,
    ILogger<BookingSettlementService> logger)
    : BackgroundService
{
    // The PDFs this process could not draw (payments Phase 6), and whether it has stopped drawing altogether.
    // Only the pass touches them, and passes never overlap.
    private readonly HashSet<RenditionCandidate> _undrawable = [];
    private bool _drawingStopped;

    // Where the next page of open payables to check again starts (payments Phase 8). Only the pass touches it.
    private int _verifyOffset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.SettleBookings)
        {
            LogDisabled(logger);
            return;
        }

        var interval = TimeSpan.FromSeconds(settings.SettlementIntervalSeconds);
        LogStarted(logger, interval);

        // A first pass on startup, before the first tick. A process that has been down over a
        // deadline should not wait another whole interval to notice.
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await SafeWaitAsync(timer, stoppingToken))
            await RunOnceAsync(stoppingToken);
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        // A scope — and so a DbContext — PER COMMAND. Settlement swallows a concurrency conflict on
        // one booking and moves on, which leaves that booking tracked with a stale token; in a shared
        // context every later save in the pass would re-issue it and fail, and one dealer clicking at
        // the wrong instant would cancel a minute of payments and reminders.
        //
        // And a FAILURE per command (Phase 3, 2026-09-26). The booking pass now records money owed —
        // a no-show's refund, a deposit released when its window closes — and a query the database
        // refused there used to end the whole tick: no refund sent, no reminder staged, every minute.
        // Each command now fails on its own, logged with its name, and the next one still runs.
        await RunAsync(new SettleDueBookingsCommand(), cancellationToken);

        // Payments after bookings, and in the same pass rather than on a timer of their own. The
        // order matters: expiring an unpaid booking is what makes its open checkout pointless, and
        // sweeping in that order closes the attempt on the same tick rather than the next. A second
        // timer would buy nothing and give two schedules to reason about.
        await RunAsync(new SettlePaymentsCommand(), cancellationToken);

        // The office payables ledger after the money and before the documents (payments Phase 8): a booking the
        // sweep above closed, and whose refunds it recorded, is judged on everything this tick knows, and a
        // statement issued below reads a penalty the ledger kept here as kept.
        await RecordPayablesAsync(cancellationToken);

        // Financial documents right after the money (payments Phase 5), so a refund settled in this pass
        // gets its receipt in this pass (when its payment already has one). The money never waits for its
        // paperwork: issuing runs after the money is recorded, never inside the transaction that records it.
        await IssueFinancialDocumentsAsync(cancellationToken);

        // Reminders after both sweeps: a booking whose payment window has just closed is expired above, so
        // it is never reminded to pay for something that is already gone. The reminders only stage
        // notifications; the outbox dispatcher sends them within seconds.
        await RunAsync(new SendDueRemindersCommand(), cancellationToken);

        // PDFs last (payments Phase 6): the heaviest step and the least urgent, so nothing a customer is
        // waiting on waits behind it — and still in the pass that issued the document, so its PDF follows it
        // within the minute.
        await RenderFinancialDocumentsAsync(cancellationToken);
    }

    /// <summary>
    /// Draws the PDFs owed (payments Phase 6): one query for the work, then one scope PER PDF, as issuing does.
    /// A PDF that cannot be drawn is a defect — logged once at Error by the step that found it, and left alone
    /// until the process restarts, because drawing it again every minute would only log it again. Storage that
    /// refuses a PDF stops the step: it would refuse the next one too, and the next tick tries again.
    /// </summary>
    /// <remarks>
    /// A FULL pass in which not one PDF could be drawn is a defect in the layout or the renderer, not in forty
    /// documents that happen to be next in line. Drawing then stops until the process restarts, said once at
    /// Error, rather than failing — and logging — every document on the platform a pass at a time, with a
    /// work query that grows by every failure. A pass of fewer PDFs, or one that draws anything, never stops it.
    /// </remarks>
    private async Task RenderFinancialDocumentsAsync(CancellationToken cancellationToken)
    {
        if (_drawingStopped)
            return;

        var work = await RunAsync(new ListFinancialDocumentRenditionWorkQuery([.. _undrawable]), cancellationToken);
        if (work is null)
            return;

        var undrawable = 0;
        foreach (var candidate in work)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var outcome = await RunAsync(new RenderFinancialDocumentCommand(candidate.DocumentId, candidate.Language, candidate.Kind), cancellationToken);
            if (outcome is null || outcome.StorageFailed)
                return;
            if (outcome.CannotBeDrawn)
            {
                _undrawable.Add(candidate);
                undrawable++;
            }
        }

        if (undrawable > 0 && undrawable == work.Count && work.Count >= documentOptions.Value.MaxRenditionsPerPass)
        {
            _drawingStopped = true;
            LogDrawingStopped(logger, undrawable);
        }
    }

    /// <summary>
    /// Issues every document owed (payments Phase 5): one query for the work, then one scope — and so one
    /// context and one transaction — PER DOCUMENT, which is what keeps each number and its document
    /// together and a series gapless. A document that cannot be issued puts its family on hold, recorded in
    /// a scope of its own: the issuing scope's context still holds the row that was not inserted. A step
    /// that throws is logged and the next one runs, as every pass here does.
    /// </summary>
    private async Task IssueFinancialDocumentsAsync(CancellationToken cancellationToken)
    {
        var work = await RunAsync(new ListFinancialDocumentWorkQuery(), cancellationToken);
        if (work is null)
            return;

        foreach (var candidate in work)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var outcome = await RunAsync(
                new IssueFinancialDocumentCommand(candidate.Type, candidate.SubjectId, candidate.BookingId),
                cancellationToken);
            if (outcome?.Hold is { } hold)
            {
                await RunAsync(
                    new RecordFinancialDocumentHoldCommand(candidate.Type, candidate.SubjectId, candidate.BookingId, hold.Reason, hold.Error),
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// Records what final bookings come to for their offices (payments Phase 8): one query for the work, then one
    /// scope — one context, one transaction — PER BOOKING, as issuing does, so a booking that cannot be recorded never
    /// holds up another. Then a page of the open payables is checked against their records again, the next page on the
    /// next tick, so every payable is looked at in turn however many there are.
    /// </summary>
    private async Task RecordPayablesAsync(CancellationToken cancellationToken)
    {
        var work = await RunAsync(new ListPayableWorkQuery(_verifyOffset), cancellationToken);
        if (work is null)
            return;

        foreach (var bookingId in work.BookingsToRecord)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            await RunAsync(new RecordOfficePayableCommand(bookingId), cancellationToken);
        }

        foreach (var payableId in work.PayablesToVerify)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            await RunAsync(new VerifyOfficePayableCommand(payableId), cancellationToken);
        }

        _verifyOffset = work.NextVerifyOffset;
    }

    private async Task<TResponse?> RunAsync<TResponse>(IRequest<TResponse> command, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down. Not a failure.
        }
#pragma warning disable CA1031 // A pass that fails must not take the service, or the next pass, down with it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogPassFailed(logger, command.GetType().Name, exception);
        }

        return default;
    }

    [LoggerMessage(
        2200,
        LogLevel.Warning,
        "Booking settlement is DISABLED. Requests, approvals, no-shows and returns will keep their old status past their deadlines.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(2201, LogLevel.Information, "Booking settlement running every {Interval}.")]
    private static partial void LogStarted(ILogger logger, TimeSpan interval);

    [LoggerMessage(2202, LogLevel.Error, "The {Pass} settlement pass failed. The other passes still ran; the next tick will retry.")]
    private static partial void LogPassFailed(ILogger logger, string pass, Exception exception);

    [LoggerMessage(
        2203,
        LogLevel.Error,
        "Not one of {Count} PDFs could be drawn in a full pass: a defect in the layout or the renderer, not in the documents. "
        + "Drawing PDFs is STOPPED until the API restarts; documents are still issued and readable on screen.")]
    private static partial void LogDrawingStopped(ILogger logger, int count);

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
