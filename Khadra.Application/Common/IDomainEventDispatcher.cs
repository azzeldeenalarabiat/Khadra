using Khadra.Domain.Common;

namespace Khadra.Application.Common;

// Dispatches events collected from aggregates AFTER the unit of work has committed them.
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
