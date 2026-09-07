using Khadra.Application.Common;
using Khadra.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Khadra.Infrastructure.Persistence;

// Commits the tracked aggregates, then dispatches their domain events. Events are dispatched only
// after a successful COMMIT, so a side effect (session revocation, mail, a notification) never runs
// for work that was rolled back.
//
// That promise needs help once a caller opens an explicit transaction. SaveChangesAsync can be
// called several times inside one, and the early saves are not durable until the transaction
// commits: the booking-creation handler expires stale holds, saves, then inserts, and the insert can
// still be refused by the exclusion constraint. Dispatching as each save returned would fire
// BookingExpired for a booking the rollback then un-expires. So while a transaction is open the
// events are HELD, and ExecuteInTransactionAsync releases them after the commit or drops them with
// the rollback.
internal sealed class UnitOfWork(KhadraDbContext context, IDomainEventDispatcher dispatcher) : IUnitOfWork
{
    private readonly List<IDomainEvent> _held = [];

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var aggregates = context.ChangeTracker
            .Entries<AggregateRoot>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();
        var domainEvents = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToList();
        aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());

        int written;
        try
        {
            written = await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The record was changed by another request.", exception);
        }
        // 23P01: an exclusion constraint refused the write because something else already holds what
        // this was claiming. Translated here, next to the concurrency conflict it is the sibling of,
        // so no handler has to know what Npgsql is -- and so it stops being an anonymous
        // DbUpdateException that the API maps to a flat "data.conflict" nobody can act on.
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation
        } postgres)
        {
            throw new ExclusiveHoldConflictException(
                "Something else already holds what this write was claiming.",
                postgres.ConstraintName,
                exception);
        }

        if (domainEvents.Count == 0)
            return written;

        // Asked of the context rather than tracked with a flag of our own, so an ambient transaction
        // opened by anything else is honoured too.
        if (context.Database.CurrentTransaction is not null)
            _held.AddRange(domainEvents);
        else
            await dispatcher.DispatchAsync(domainEvents, cancellationToken);

        return written;
    }

    /// <remarks>
    /// The delegate must be safe to run more than once before anyone enables EF's retry-on-failure:
    /// nothing resets the change tracker between attempts, so a second attempt would re-add whatever
    /// the first one added. No retrying strategy is configured today (pre-launch checklist item 65).
    /// </remarks>
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async token =>
        {
            _held.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            try
            {
                await work(token);
                await transaction.CommitAsync(token);
            }
            catch
            {
                // The transaction disposes into a rollback; the events of the writes it is undoing
                // must go with it, or a handler reacts to something that never happened.
                _held.Clear();
                throw;
            }

            if (_held.Count == 0)
                return;

            // Copied out and cleared BEFORE dispatch: a handler that saves again must not find the
            // events it is itself reacting to still queued.
            var released = _held.ToList();
            _held.Clear();
            await dispatcher.DispatchAsync(released, token);
        }, cancellationToken);
    }
}
