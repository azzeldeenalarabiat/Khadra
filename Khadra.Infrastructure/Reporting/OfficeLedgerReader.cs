using Khadra.Application.Common;
using Khadra.Application.Payables.ReadModels;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Payables;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>
/// The office payables ledger as every screen reads it (payments Phase 8): the administrator's balances, lists and
/// finance figures, the office's payouts, and one booking's money card.
/// </summary>
/// <remarks>
/// <para>
/// Every query is a scoped, sequential read of this request's context, as every reader here is. Whether an open
/// payable is due is decided by <see cref="PayableStates.Of"/> from three facts read here each time — its open
/// holds, and its booking's outstanding refunds and live disputes — never from a stored flag.
/// </para>
/// <para>
/// Names — the office's, an administrator's — are read past the soft-delete filter, deliberately: money owed to an
/// office that has left the platform is still owed, and a settlement still names who recorded it.
/// </para>
/// </remarks>
internal sealed class OfficeLedgerReader(KhadraDbContext context) : IOfficeLedgerReader
{
    public async Task<BookingLedger> ForBookingAsync(Id bookingId, CancellationToken cancellationToken = default)
    {
        var payable = await context.OfficePayables
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.BookingId == bookingId, cancellationToken);
        var holds = await HoldsAsync(context.OfficePayableHolds.Where(hold => hold.BookingId == bookingId), cancellationToken);
        var blocks = await BlocksAsync([bookingId], cancellationToken);
        if (payable is null)
            return new BookingLedger(null, holds, blocks);

        var composed = await ComposeAsync([payable], cancellationToken);
        return new BookingLedger(composed[0], holds, blocks);
    }

    public async Task<LedgerPayable?> PayableAsync(Id payableId, CancellationToken cancellationToken = default)
    {
        var payable = await context.OfficePayables
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == payableId, cancellationToken);
        return payable is null ? null : (await ComposeAsync([payable], cancellationToken))[0];
    }

    public async Task<RecordedPayable?> RecordedAsync(Id bookingId, CancellationToken cancellationToken = default)
    {
        var found = await context.OfficePayables
            .AsNoTracking()
            .Where(payable => payable.BookingId == bookingId)
            .Select(payable => new { payable.Id, payable.Outcome, payable.Commission, payable.Currency, payable.RecordedAt })
            .FirstOrDefaultAsync(cancellationToken);
        return found is null
            ? null
            : new RecordedPayable(found.Id, found.Outcome, Money.Create(found.Commission, found.Currency), found.RecordedAt);
    }

    public async Task<IReadOnlyList<OfficeBalance>> BalancesAsync(Id? dealerId, CancellationToken cancellationToken = default)
    {
        var open = await context.OfficePayables
            .AsNoTracking()
            .Where(payable => payable.SettlementId == null && (dealerId == null || payable.DealerId == dealerId))
            .Select(payable => new { payable.Id, payable.BookingId, payable.DealerId, payable.Currency, payable.Provider, payable.Net })
            .ToListAsync(cancellationToken);
        var held = await HeldPayableIdsAsync(open.Select(payable => payable.Id).ToList(), cancellationToken);
        var blocked = (await BlocksAsync(open.Select(payable => payable.BookingId).Distinct().ToList(), cancellationToken))
            .Select(block => block.BookingId)
            .ToHashSet();

        var settlements = await StandingSettlements()
            .Where(settlement => dealerId == null || settlement.DealerId == dealerId)
            .Select(settlement => new
            {
                settlement.Id,
                settlement.DealerId,
                settlement.Currency,
                settlement.Provider,
                settlement.Number,
                settlement.Direction,
                settlement.Amount,
                settlement.PaidOn,
                settlement.RecordedAt,
            })
            .ToListAsync(cancellationToken);

        var keys = open.Select(payable => (payable.DealerId, payable.Currency, payable.Provider))
            .Concat(settlements.Select(settlement => (settlement.DealerId, settlement.Currency, settlement.Provider)))
            .Distinct()
            .ToList();
        var names = await OfficeNamesAsync(keys.Select(key => key.DealerId).Distinct().ToList(), cancellationToken);

        var balances = new List<OfficeBalance>();
        foreach (var (office, currency, provider) in keys)
        {
            var mine = open.Where(payable => payable.DealerId == office && payable.Currency == currency && payable.Provider == provider).ToList();
            var states = mine
                .Select(payable => (payable.Net, State: PayableStates.Of(false, held.Contains(payable.Id), blocked.Contains(payable.BookingId), payable.Net)))
                .ToList();
            var due = states.Where(entry => entry.State == PayableStates.Due).ToList();
            var notYet = states.Where(entry => entry.State is PayableStates.OnHold or PayableStates.Blocked).ToList();
            var last = settlements
                .Where(settlement => settlement.DealerId == office && settlement.Currency == currency && settlement.Provider == provider)
                .OrderByDescending(settlement => settlement.RecordedAt)
                .ThenByDescending(settlement => settlement.Id.Value)
                .FirstOrDefault();

            balances.Add(new OfficeBalance(
                office,
                names.GetValueOrDefault(office) ?? string.Empty,
                currency,
                provider,
                PaymentProviders.IsSandbox(provider),
                due.Count,
                due.Sum(entry => entry.Net),
                notYet.Count,
                notYet.Sum(entry => entry.Net),
                last is null ? null : new LedgerSettlementSummary(last.Id, last.Number, last.Direction, last.Amount, last.PaidOn)));
        }

        // Real money before test money, then the office, then the currency: the order an administrator reads.
        return balances
            .OrderBy(balance => balance.IsTest)
            .ThenBy(balance => balance.DealerName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(balance => balance.Currency, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<PagedResult<LedgerPayable>> ListPayablesAsync(PayableListFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = context.OfficePayables.AsNoTracking();
        if (filter.DealerId is { } dealerId)
            query = query.Where(payable => payable.DealerId == dealerId);
        if (filter.Scope == PayableListScopes.Open)
            query = query.Where(payable => payable.SettlementId == null);
        else if (filter.Scope == PayableListScopes.Settled)
            query = query.Where(payable => payable.SettlementId != null);
        if (filter.FinalFrom is { } from)
            query = query.Where(payable => payable.FinalAt >= from);
        if (filter.FinalBefore is { } before)
            query = query.Where(payable => payable.FinalAt < before);

        var total = await query.CountAsync(cancellationToken);
        var page = await query
            .Include(payable => payable.Lines)
            .OrderByDescending(payable => payable.FinalAt)
            .ThenByDescending(payable => payable.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<LedgerPayable>(await ComposeAsync(page, cancellationToken), filter.Page, filter.PageSize, total);
    }

    public async Task<IReadOnlyList<LedgerHold>> ListBookingHoldsAsync(Id? dealerId, CancellationToken cancellationToken = default) =>
        await HoldsAsync(
            context.OfficePayableHolds.Where(hold => hold.PayableId == null && (dealerId == null || hold.DealerId == dealerId)),
            cancellationToken);

    public async Task<PagedResult<LedgerSettlement>> ListSettlementsAsync(
        Id dealerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.OfficeSettlements.AsNoTracking().Where(settlement => settlement.DealerId == dealerId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(settlement => settlement.RecordedAt)
            .ThenByDescending(settlement => settlement.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<LedgerSettlement>(await SettlementsAsync(rows, cancellationToken), page, pageSize, total);
    }

    public async Task<LedgerSettlementDetail?> SettlementAsync(Id settlementId, CancellationToken cancellationToken = default)
    {
        var settlement = await context.OfficeSettlements
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == settlementId, cancellationToken);
        if (settlement is null)
            return null;

        var summary = (await SettlementsAsync([settlement], cancellationToken))[0];
        var payableIds = settlement.Lines.Select(line => line.PayableId).ToList();
        var payables = await context.OfficePayables
            .AsNoTracking()
            .Where(payable => payableIds.Contains(payable.Id))
            .Select(payable => new { payable.Id, payable.BookingId, payable.BookingReference, payable.Outcome, payable.FinalAt })
            .ToListAsync(cancellationToken);
        var lines = settlement.Lines
            .Join(payables, line => line.PayableId, payable => payable.Id, (line, payable) => (line, payable))
            .OrderBy(entry => entry.payable.FinalAt)
            .ThenBy(entry => entry.payable.Id.Value)
            .Select(entry => new LedgerSettlementLine(
                entry.payable.Id, entry.payable.BookingId, entry.payable.BookingReference, entry.payable.Outcome, entry.line.Net))
            .ToList();
        return new LedgerSettlementDetail(summary, lines);
    }

    public async Task<FinanceSummary> FinanceSummaryAsync(
        DateOnly fromDay,
        DateOnly toDay,
        DateTimeOffset from,
        DateTimeOffset before,
        CancellationToken cancellationToken = default)
    {
        var recorded = await context.OfficePayables
            .AsNoTracking()
            .Where(payable => payable.FinalAt >= from && payable.FinalAt < before)
            .Select(payable => new { payable.Currency, payable.Provider, payable.Commission, payable.OfficeMoney, payable.OfficeCharges })
            .ToListAsync(cancellationToken);

        var moved = await StandingSettlements()
            .Where(settlement => settlement.PaidOn >= fromDay && settlement.PaidOn <= toDay)
            .Select(settlement => new { settlement.Currency, settlement.Provider, settlement.Direction, settlement.Amount })
            .ToListAsync(cancellationToken);

        var kept = await KeptFromDisputesAsync(from, before, cancellationToken);
        var balances = await BalancesAsync(null, cancellationToken);

        var totals = new Dictionary<(string Currency, bool IsTest), FinanceBuilder>();
        FinanceBuilder For(string currency, string provider)
        {
            var key = (currency, PaymentProviders.IsSandbox(provider));
            if (!totals.TryGetValue(key, out var builder))
                totals[key] = builder = new FinanceBuilder();
            return builder;
        }

        foreach (var payable in recorded)
        {
            var builder = For(payable.Currency, payable.Provider);
            builder.Commission += payable.Commission;
            builder.OfficeMoney += payable.OfficeMoney;
            builder.OfficeCharges += payable.OfficeCharges;
            builder.Recorded++;
        }

        foreach (var settlement in moved)
        {
            var builder = For(settlement.Currency, settlement.Provider);
            if (settlement.Direction == SettlementDirection.Payout)
                builder.Paid += settlement.Amount;
            else if (settlement.Direction == SettlementDirection.Received)
                builder.Received -= settlement.Amount;
        }

        foreach (var (currency, provider, amount) in kept)
            For(currency, provider).Kept += amount;

        // What is owed right now, per office: everything open, due or not yet, since held and blocked money is still
        // owed — to the office when its open net is above zero, by it when below.
        foreach (var office in balances.GroupBy(balance => (balance.DealerId, balance.Currency, balance.Provider)))
        {
            var net = office.Sum(balance => balance.DueNet + balance.NotYetDueNet);
            var builder = For(office.Key.Currency, office.Key.Provider);
            if (net > 0m)
                builder.OwedTo += net;
            else
                builder.OwedBy -= net;
        }

        var openHolds = await context.OfficePayableHolds
            .AsNoTracking()
            .Where(hold => hold.ReleasedAt == null)
            .Select(hold => new { hold.BookingId, hold.PayableId })
            .ToListAsync(cancellationToken);
        var openPayables = await context.OfficePayables
            .AsNoTracking()
            .Where(payable => payable.SettlementId == null)
            .Select(payable => payable.BookingId)
            .ToListAsync(cancellationToken);
        var blocked = (await BlocksAsync(openPayables, cancellationToken)).Select(block => block.BookingId).ToHashSet();

        return new FinanceSummary(
            totals
                .OrderBy(entry => entry.Key.IsTest)
                .ThenBy(entry => entry.Key.Currency, StringComparer.Ordinal)
                .Select(entry => entry.Value.Build(entry.Key.Currency, entry.Key.IsTest))
                .ToList(),
            openHolds.Count(hold => hold.PayableId == null),
            openHolds.Where(hold => hold.PayableId != null).Select(hold => hold.PayableId).Distinct().Count(),
            openPayables.Count(blocked.Contains));
    }

    public async Task<IReadOnlyList<PayableBlock>> BlocksAsync(IReadOnlyCollection<Id> bookingIds, CancellationToken cancellationToken = default)
    {
        if (bookingIds.Count == 0)
            return [];

        var ids = bookingIds.ToList();
        var settled = RefundStatus.Settled;
        var refunds = await context.Set<Refund>()
            .AsNoTracking()
            .Where(refund => refund.Status != settled)
            .Join(
                context.Payments.Where(payment => ids.Contains(payment.BookingId)),
                refund => refund.PaymentId,
                payment => payment.Id,
                (refund, payment) => new { payment.BookingId, RefundId = refund.Id, refund.RequestedAt })
            .ToListAsync(cancellationToken);

        var open = DisputeStatus.Open;
        var underReview = DisputeStatus.UnderReview;
        var disputes = await context.DisputeTickets
            .AsNoTracking()
            .Where(ticket => ids.Contains(ticket.BookingId) && (ticket.Status == open || ticket.Status == underReview))
            .Select(ticket => ticket.BookingId)
            .ToListAsync(cancellationToken);

        return refunds
            .OrderBy(refund => refund.RequestedAt)
            .Select(refund => new PayableBlock(refund.BookingId, PayableBlockKinds.RefundOutstanding, refund.RefundId))
            .Concat(disputes.Distinct().Select(booking => new PayableBlock(booking, PayableBlockKinds.DisputeLive, null)))
            .ToList();
    }

    public async Task<string?> OfficeNameAsync(Id dealerId, CancellationToken cancellationToken = default) =>
        (await OfficeNamesAsync([dealerId], cancellationToken)).GetValueOrDefault(dealerId);

    public async Task<PayableHoldsSummary> SystemHoldsSummaryAsync(CancellationToken cancellationToken = default)
    {
        var manual = PayableHoldReason.Manual;
        var holds = await HoldsAsync(context.OfficePayableHolds.Where(hold => hold.Reason != manual), cancellationToken);
        return holds.Count == 0
            ? PayableHoldsSummary.None
            : new PayableHoldsSummary(
                holds.Count,
                holds.Select(hold => hold.BookingId).Distinct().ToList(),
                holds.Select(hold => hold.BookingReference).OfType<string>().Distinct(StringComparer.Ordinal).ToList(),
                holds.Min(hold => hold.OpenedAt));
    }

    // ── Composition ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Recorded payables with their office's name, their settlement, their open holds and their blocks.</summary>
    private async Task<IReadOnlyList<LedgerPayable>> ComposeAsync(List<OfficePayable> payables, CancellationToken cancellationToken)
    {
        if (payables.Count == 0)
            return [];

        var names = await OfficeNamesAsync(payables.Select(payable => payable.DealerId).Distinct().ToList(), cancellationToken);
        // Nullable, as the column is, so the list is compared with it directly.
        var payableIds = payables.Select(payable => (Id?)payable.Id).ToList();
        var holds = await HoldsAsync(
            context.OfficePayableHolds.Where(hold => payableIds.Contains(hold.PayableId)),
            cancellationToken);
        var blocks = await BlocksAsync(payables.Select(payable => payable.BookingId).Distinct().ToList(), cancellationToken);
        var settlementIds = payables.Where(payable => payable.SettlementId is not null).Select(payable => payable.SettlementId!.Value).Distinct().ToList();
        var settlements = settlementIds.Count == 0
            ? []
            : await context.OfficeSettlements
                .AsNoTracking()
                .Where(settlement => settlementIds.Contains(settlement.Id))
                .Select(settlement => new LedgerSettlementRef(settlement.Id, settlement.Number, settlement.PaidOn))
                .ToListAsync(cancellationToken);

        return payables
            .Select(payable =>
            {
                var itsHolds = holds.Where(hold => hold.PayableId == payable.Id).ToList();
                var itsBlocks = blocks.Where(block => block.BookingId == payable.BookingId).ToList();
                return new LedgerPayable(
                    payable.Id,
                    payable.BookingId,
                    payable.BookingReference,
                    payable.DealerId,
                    names.GetValueOrDefault(payable.DealerId) ?? string.Empty,
                    payable.Outcome,
                    payable.Currency,
                    payable.IsTest,
                    payable.OfficeMoney,
                    payable.Commission,
                    payable.OfficeCharges,
                    payable.Net,
                    payable.FinalAt,
                    payable.RecordedAt,
                    payable.CalculatorVersion,
                    PayableStates.Of(payable.IsSettled, itsHolds.Count > 0, itsBlocks.Count > 0, payable.Net),
                    payable.Lines.Select(line => new LedgerLine(line.Kind, line.Amount, line.SourceId)).ToList(),
                    payable.SettlementId is { } settlementId ? settlements.FirstOrDefault(settlement => settlement.SettlementId == settlementId) : null,
                    itsHolds,
                    itsBlocks);
            })
            .ToList();
    }

    /// <summary>The OPEN holds a query selects, oldest first, with their bookings' references and their administrators' names.</summary>
    private async Task<IReadOnlyList<LedgerHold>> HoldsAsync(IQueryable<OfficePayableHold> holds, CancellationToken cancellationToken)
    {
        var rows = await holds
            .AsNoTracking()
            .Where(hold => hold.ReleasedAt == null)
            .OrderBy(hold => hold.OpenedAt)
            .ThenBy(hold => hold.Id)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return [];

        var bookingIds = rows.Select(hold => hold.BookingId).Distinct().ToList();
        // Bookings are never deleted, so no filter is bypassed here.
        var references = await context.Bookings
            .AsNoTracking()
            .Where(booking => bookingIds.Contains(booking.Id))
            .Select(booking => new { booking.Id, Reference = booking.Reference.Value })
            .ToDictionaryAsync(booking => booking.Id, booking => booking.Reference, cancellationToken);
        var admins = await PeopleAsync(rows.Where(hold => hold.OpenedByAdminId is not null).Select(hold => hold.OpenedByAdminId!.Value), cancellationToken);

        return rows
            .Select(hold => new LedgerHold(
                hold.Id,
                hold.BookingId,
                references.GetValueOrDefault(hold.BookingId),
                hold.PayableId,
                hold.Reason,
                hold.Detail,
                hold.OpenedAt,
                hold.OpenedByAdminId is { } admin ? admins.GetValueOrDefault(admin) : null))
            .ToList();
    }

    private async Task<IReadOnlyList<LedgerSettlement>> SettlementsAsync(List<OfficeSettlement> settlements, CancellationToken cancellationToken)
    {
        if (settlements.Count == 0)
            return [];

        var ids = settlements.Select(settlement => settlement.Id).ToList();
        var voids = await context.OfficeSettlementVoids
            .AsNoTracking()
            .Where(voided => ids.Contains(voided.Id))
            .ToListAsync(cancellationToken);
        var counts = await context.Set<OfficeSettlementLine>()
            .AsNoTracking()
            .Where(line => ids.Contains(line.SettlementId))
            .GroupBy(line => line.SettlementId)
            .Select(group => new { SettlementId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.SettlementId, entry => entry.Count, cancellationToken);
        var names = await OfficeNamesAsync(settlements.Select(settlement => settlement.DealerId).Distinct().ToList(), cancellationToken);
        var people = await PeopleAsync(
            settlements.Select(settlement => settlement.RecordedByAdminId).Concat(voids.Select(voided => voided.VoidedByAdminId)),
            cancellationToken);

        return settlements
            .Select(settlement =>
            {
                var voided = voids.FirstOrDefault(candidate => candidate.Id == settlement.Id);
                return new LedgerSettlement(
                    settlement.Id,
                    settlement.Number,
                    settlement.DealerId,
                    names.GetValueOrDefault(settlement.DealerId) ?? string.Empty,
                    settlement.Currency,
                    settlement.IsTest,
                    settlement.Direction,
                    settlement.Amount,
                    settlement.PaidOn,
                    settlement.Reference,
                    settlement.Note,
                    settlement.RecordedAt,
                    people.GetValueOrDefault(settlement.RecordedByAdminId),
                    counts.GetValueOrDefault(settlement.Id),
                    voided is null ? null : new LedgerSettlementVoid(voided.VoidedAt, people.GetValueOrDefault(voided.VoidedByAdminId), voided.Reason));
            })
            .ToList();
    }

    /// <summary>What resolved disputes left with the platform, per currency and kind of money, by when they were decided.</summary>
    private async Task<IReadOnlyList<(string Currency, string Provider, decimal Amount)>> KeptFromDisputesAsync(
        DateTimeOffset from,
        DateTimeOffset before,
        CancellationToken cancellationToken)
    {
        // The resolution is a JSON document, read whole: decided in the span, filtered here.
        var resolved = DisputeStatus.Resolved;
        var tickets = await context.DisputeTickets
            .AsNoTracking()
            .Where(ticket => ticket.Status == resolved)
            .ToListAsync(cancellationToken);
        var decided = tickets
            .Where(ticket => ticket.Resolution is { } resolution &&
                resolution.ResolvedAt >= from && resolution.ResolvedAt < before &&
                !resolution.Deposit.RetainedByPlatform.IsZero)
            .ToList();
        if (decided.Count == 0)
            return [];

        var bookingIds = decided.Select(ticket => ticket.BookingId).Distinct().ToList();
        var providers = await context.Bookings
            .AsNoTracking()
            .Where(booking => bookingIds.Contains(booking.Id) && booking.DepositPaymentId != null)
            .Join(context.Payments, booking => booking.DepositPaymentId, payment => payment.Id, (booking, payment) => new { booking.Id, payment.Provider })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Provider, cancellationToken);

        return decided
            .Where(ticket => providers.ContainsKey(ticket.BookingId))
            .Select(ticket => (
                ticket.Resolution!.Deposit.RetainedByPlatform.CurrencyCode,
                providers[ticket.BookingId],
                ticket.Resolution.Deposit.RetainedByPlatform.Amount))
            .ToList();
    }

    /// <summary>Settlements that stand: every one not voided.</summary>
    private IQueryable<OfficeSettlement> StandingSettlements() =>
        context.OfficeSettlements
            .AsNoTracking()
            .Where(settlement => !context.OfficeSettlementVoids.Any(voided => voided.Id == settlement.Id));

    private async Task<HashSet<Id>> HeldPayableIdsAsync(List<Id> payableIds, CancellationToken cancellationToken)
    {
        if (payableIds.Count == 0)
            return [];

        var ids = payableIds.Select(id => (Id?)id).ToList();
        var held = await context.OfficePayableHolds
            .AsNoTracking()
            .Where(hold => hold.ReleasedAt == null && ids.Contains(hold.PayableId))
            .Select(hold => hold.PayableId)
            .ToListAsync(cancellationToken);
        return held.OfType<Id>().ToHashSet();
    }

    private async Task<Dictionary<Id, string>> OfficeNamesAsync(List<Id> dealerIds, CancellationToken cancellationToken)
    {
        if (dealerIds.Count == 0)
            return [];

        var ids = dealerIds.ToList();
        // Past the soft-delete filter, deliberately: money owed to an office that left the platform is still owed.
        return await context.Dealers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(dealer => ids.Contains(dealer.Id))
            .Select(dealer => new { dealer.Id, Name = dealer.BusinessName.Value })
            .ToDictionaryAsync(dealer => dealer.Id, dealer => dealer.Name, cancellationToken);
    }

    private async Task<Dictionary<Id, string>> PeopleAsync(IEnumerable<Id> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        // Past the soft-delete filter, deliberately: a settlement still names the administrator who recorded it.
        return await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new { user.Id, Name = user.Name.Value })
            .ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken);
    }

    private sealed class FinanceBuilder
    {
        public decimal Commission { get; set; }
        public decimal Kept { get; set; }
        public decimal OfficeMoney { get; set; }
        public decimal OfficeCharges { get; set; }
        public decimal Paid { get; set; }
        public decimal Received { get; set; }
        public decimal OwedTo { get; set; }
        public decimal OwedBy { get; set; }
        public int Recorded { get; set; }

        public FinanceTotals Build(string currency, bool isTest) =>
            new(currency, isTest, Commission, Kept, OfficeMoney, OfficeCharges, Paid, Received, OwedTo, OwedBy, Recorded);
    }
}
