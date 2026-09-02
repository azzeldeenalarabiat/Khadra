using Khadra.Application.Common;
using Khadra.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence;

// Commits the tracked aggregates, then dispatches their domain events. Events are dispatched only
// after a successful commit so side effects (session revocation, mail) never run for rolled-back work.
internal sealed class UnitOfWork(KhadraDbContext context, IDomainEventDispatcher dispatcher) : IUnitOfWork
{
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

        if (domainEvents.Count > 0)
            await dispatcher.DispatchAsync(domainEvents, cancellationToken);

        return written;
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            await work(token);
            await transaction.CommitAsync(token);
        }, cancellationToken);
    }
}
