using System.Globalization;
using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.Payables.Dtos;
using Khadra.Application.Payables.ReadModels;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.FinancialDocuments.Repositories;
using Khadra.Domain.Payables;
using Khadra.Domain.Payables.Repositories;
using Khadra.Domain.Payments;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Payables.Settlements;

/// <summary>
/// An administrator records that everything due to or from one office, in one currency and one kind of money, was
/// settled by hand on <paramref name="PaidOn"/> (payments Phase 8).
/// </summary>
/// <param name="Provider">The kind of money, as the office's balance named it: SANDBOX for test money.</param>
/// <param name="ExpectedAmount">
/// The balance the administrator saw and confirms, signed from Khadra's side. Anything else due now is refused with
/// the balance due now: the platform never records a figure nobody saw.
/// </param>
public sealed record RecordOfficeSettlementCommand(
    Id DealerId,
    string Currency,
    string Provider,
    decimal ExpectedAmount,
    DateOnly PaidOn,
    string? Reference,
    string? Note,
    Id AdminUserId) : ICommand<Result<OfficeSettlementDetailDto, Error>>;

public sealed class RecordOfficeSettlementCommandValidator : AbstractValidator<RecordOfficeSettlementCommand>
{
    public RecordOfficeSettlementCommandValidator()
    {
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(OfficePayable.ProviderMaxLength);
        RuleFor(command => command.ExpectedAmount)
            .Must(amount => decimal.Round(amount, OfficePayable.AmountScale) == amount)
            .WithMessage($"An amount has at most {OfficePayable.AmountScale} decimals.");
        RuleFor(command => command.PaidOn).NotEqual(default(DateOnly)).WithMessage("Say which day the money moved.");
        RuleFor(command => command.Reference).MaximumLength(OfficeSettlement.ReferenceMaxLength);
        RuleFor(command => command.Note).MaximumLength(OfficeSettlement.NoteMaxLength);
    }
}

/// <summary>An administrator voids a settlement recorded wrongly; its payables are due again (payments Phase 8).</summary>
public sealed record VoidOfficeSettlementCommand(Id SettlementId, Id AdminUserId, string? Reason)
    : ICommand<Result<OfficeSettlementDetailDto, Error>>;

public sealed class VoidOfficeSettlementCommandValidator : AbstractValidator<VoidOfficeSettlementCommand>
{
    public VoidOfficeSettlementCommandValidator() =>
        RuleFor(command => command.Reason)
            .NotEmpty().WithMessage("Say why the settlement is being voided.")
            .MaximumLength(OfficeSettlementVoid.MaxReasonLength);
}

/// <summary>
/// Records a settlement with one office (payments Phase 8; owner, 2026-09-24 and 2026-09-29): every payable due in
/// the currency and kind of money, netted, in ONE transaction with its number and its audit entry.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is due</b> is decided here and nowhere else: the office's open payables, less those an open hold names,
/// less those whose booking has a refund outstanding or a dispute live (read live, never from a stored flag), less
/// the net-zero ones, which nothing will ever move for. Partial settlement is not offered (pre-launch item 206).
/// </para>
/// <para>
/// <b>Only what was seen.</b> The administrator confirms the balance they were shown; if what is due now differs —
/// a booking became due, was held, or another administrator settled first — nothing is recorded and the balance due
/// now is returned. Each payable is also checked against its booking's records once more, so money never moves on a
/// payable its records have since contradicted.
/// </para>
/// <para>
/// <b>Once.</b> Every payable carries a concurrency token: two administrators settling the same office at once both
/// pass the checks, and the second's save is refused whole, its number with it — the series stays gapless.
/// </para>
/// </remarks>
public sealed partial class RecordOfficeSettlementHandler(
    IOfficeLedgerReader ledger,
    IOfficePayableRepository payables,
    IOfficePayableHoldRepository holds,
    IOfficeSettlementRepository settlements,
    IFinancialDocumentFactsReader facts,
    IFinancialDocumentSeries series,
    AdminActionRecorder audit,
    IUnitOfWork unitOfWork,
    IReportingCalendar calendar,
    IClock clock,
    ILogger<RecordOfficeSettlementHandler> logger)
    : IRequestHandler<RecordOfficeSettlementCommand, Result<OfficeSettlementDetailDto, Error>>
{
    public async Task<Result<OfficeSettlementDetailDto, Error>> Handle(
        RecordOfficeSettlementCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        var today = calendar.Today(now);

        if (await ledger.OfficeNameAsync(request.DealerId, cancellationToken) is null)
            return PayableErrors.OfficeNotFound;
        if (request.PaidOn > today)
            return PayableErrors.PaidOnInFuture;

        var due = await DueAsync(request.DealerId, request.Currency, request.Provider, cancellationToken);
        if (due.Count == 0)
            return PayableErrors.NothingDue;

        var amount = due.Sum(payable => payable.Net);
        if (amount != request.ExpectedAmount)
            return PayableErrors.BalanceChanged(amount, request.Currency);

        foreach (var payable in due)
        {
            var money = await facts.BookingAsync(payable.BookingId, cancellationToken);
            var difference = money is null
                ? "the booking can no longer be read"
                : PayableComparison.Difference(
                    payable,
                    BookingFinancialsCalculator.Calculate(money.Booking, money.Payments, money.ResolvedTickets, money.HasLiveDispute, now));
            if (difference is not null)
            {
                LogRecordsChanged(logger, payable.BookingReference, difference);
                return PayableErrors.RecordsChanged;
            }
        }

        var settlementId = Id.New();
        var isTest = PaymentProviders.IsSandbox(request.Provider);
        string? number = null;
        try
        {
            // Runs ONCE: the transaction strategy does not retry, and this delegate must not be retried — a second run
            // would track a second settlement and SettleUnder would refuse payables the first already claimed. Enable
            // retry on failure for this context and this has to become re-runnable first.
            await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    var seriesKey = OfficeSettlement.SeriesKey(today.Year, isTest);
                    number = FinancialDocumentNumbers.Format(seriesKey, await series.TakeNextAsync(seriesKey, now, token));

                    // Once more, holding the series row that serialises settlements: a hold or a block that landed
                    // since the check above takes its payable out of what is due, and nothing is recorded — the
                    // number goes back with the transaction.
                    var still = await DueAsync(request.DealerId, request.Currency, request.Provider, token);
                    if (!still.Select(payable => payable.Id).ToHashSet().SetEquals(due.Select(payable => payable.Id)))
                        throw new DueChangedException(still.Sum(payable => payable.Net));

                    var settlement = OfficeSettlement.Record(
                        settlementId, number, request.DealerId, due, request.PaidOn, today, request.Reference, request.Note, request.AdminUserId, now);
                    if (settlement.IsFailure)
                        throw new DomainException($"A settlement checked above was refused: {settlement.Error.Code}.");

                    settlements.Add(settlement.Value);
                    foreach (var payable in due)
                    {
                        if (payable.SettleUnder(settlementId, now).IsFailure)
                            throw new DomainException($"Payable {payable.Id} was settled while it was being settled.");
                    }

                    // Labelled by the NUMBER, like a document's void: an entry that can never be erased names no one.
                    audit.Record(
                        AuditAction.OfficeSettlementRecorded,
                        AuditEntityType.OfficeSettlement,
                        settlementId,
                        number,
                        null,
                        Amount(amount, request.Currency),
                        settlement.Value.Note);
                    await unitOfWork.SaveChangesAsync(token);
                },
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return PayableErrors.ChangedConcurrently;
        }
        catch (DueChangedException changed)
        {
            return PayableErrors.BalanceChanged(changed.Current, request.Currency);
        }

        LogRecorded(logger, number!, amount, request.Currency, due.Count);
        var detail = await ledger.SettlementAsync(settlementId, cancellationToken);
        return OfficeSettlementDetailDto.ForAdmin(detail!);
    }

    /// <summary>The office's payables due now in the currency and kind of money: the one definition of due.</summary>
    private async Task<List<OfficePayable>> DueAsync(Id dealerId, string currency, string provider, CancellationToken cancellationToken)
    {
        var open = await payables.ListOpenForOfficeAsync(dealerId, currency, provider, cancellationToken);
        if (open.Count == 0)
            return [];

        var held = (await holds.ListOpenForOfficeAsync(dealerId, cancellationToken))
            .Where(hold => hold.PayableId is not null)
            .Select(hold => hold.PayableId!.Value)
            .ToHashSet();
        var blocked = (await ledger.BlocksAsync(open.Select(payable => payable.BookingId).Distinct().ToList(), cancellationToken))
            .Select(block => block.BookingId)
            .ToHashSet();

        return open
            .Where(payable => PayableStates.Of(false, held.Contains(payable.Id), blocked.Contains(payable.BookingId), payable.Net) == PayableStates.Due)
            .ToList();
    }

    internal static string Amount(decimal amount, string currency) =>
        string.Create(CultureInfo.InvariantCulture, $"{amount:0.000} {currency}");

    /// <summary>What is due changed inside the transaction: thrown to roll it back, its number with it.</summary>
    private sealed class DueChangedException(decimal current) : Exception("What is due changed while the settlement was being recorded.")
    {
        public decimal Current { get; } = current;
    }

    [LoggerMessage(2810, LogLevel.Information, "Settlement {Number} recorded: {Amount} {Currency} over {Count} payables.")]
    private static partial void LogRecorded(ILogger logger, string number, decimal amount, string currency, int count);

    [LoggerMessage(
        2811,
        LogLevel.Warning,
        "A settlement was refused: booking {Reference}'s payable no longer matches its records ({Difference}). The payables pass will hold it.")]
    private static partial void LogRecordsChanged(ILogger logger, string reference, string difference);
}

/// <summary>
/// Voids a settlement (payments Phase 8): the void, every payable it closed opened again, and the audit entry, in
/// ONE save. Nothing is deleted or edited; the settlement stays readable, marked void.
/// </summary>
public sealed partial class VoidOfficeSettlementHandler(
    IOfficeSettlementRepository settlements,
    IOfficePayableRepository payables,
    IOfficeLedgerReader ledger,
    AdminActionRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<VoidOfficeSettlementHandler> logger)
    : IRequestHandler<VoidOfficeSettlementCommand, Result<OfficeSettlementDetailDto, Error>>
{
    public async Task<Result<OfficeSettlementDetailDto, Error>> Handle(
        VoidOfficeSettlementCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;

        var settlement = await settlements.GetAsync(request.SettlementId, cancellationToken);
        if (settlement is null)
            return PayableErrors.SettlementNotFound;
        if (await settlements.IsVoidedAsync(settlement.Id, cancellationToken))
            return PayableErrors.SettlementAlreadyVoided;

        var voided = OfficeSettlementVoid.Record(settlement.Id, request.AdminUserId, request.Reason, now);
        if (voided.IsFailure)
            return voided.Error;

        foreach (var payable in await payables.ListClosedByAsync(settlement.Id, cancellationToken))
            payable.ReopenAfterVoid(settlement.Id);
        settlements.AddVoid(voided.Value);
        audit.Record(
            AuditAction.OfficeSettlementVoided,
            AuditEntityType.OfficeSettlement,
            settlement.Id,
            settlement.Number,
            RecordOfficeSettlementHandler.Amount(settlement.Amount, settlement.Currency),
            null,
            voided.Value.Reason);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintConflictException)
        {
            // Another administrator's void landed first: the void's key is the settlement's id.
            return PayableErrors.SettlementAlreadyVoided;
        }
        catch (ConcurrencyConflictException)
        {
            return PayableErrors.ChangedConcurrently;
        }

        LogVoided(logger, settlement.Number);
        var detail = await ledger.SettlementAsync(settlement.Id, cancellationToken);
        return OfficeSettlementDetailDto.ForAdmin(detail!);
    }

    [LoggerMessage(2812, LogLevel.Information, "Settlement {Number} was voided; its payables are due again.")]
    private static partial void LogVoided(ILogger logger, string number);
}
