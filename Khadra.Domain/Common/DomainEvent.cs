namespace Khadra.Domain.Common;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
