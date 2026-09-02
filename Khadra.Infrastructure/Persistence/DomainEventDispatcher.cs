using System.Collections.Concurrent;
using System.Reflection;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Khadra.Infrastructure.Persistence;

// Resolves IDomainEventHandler<TEvent> implementations from the scoped container and invokes them in
// process. Cross-context integration through an outbox is a later stage (see plan §13).
internal sealed class DomainEventDispatcher(IServiceProvider serviceProvider) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, (Type HandlerType, MethodInfo Handle)> Cache = new();

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (var domainEvent in domainEvents)
        {
            var (handlerType, handle) = Cache.GetOrAdd(domainEvent.GetType(), eventType =>
            {
                var contract = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
                return (contract, contract.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!);
            });

            foreach (var handler in serviceProvider.GetServices(handlerType))
            {
                if (handler is null)
                    continue;

                await (Task)handle.Invoke(handler, [domainEvent, cancellationToken])!;
            }
        }
    }
}
