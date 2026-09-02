namespace Khadra.Domain.Common;

// Aggregates that users can "delete" are hidden by a global query filter, never removed.
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAt { get; }
}
