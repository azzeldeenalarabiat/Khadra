namespace Khadra.Domain.Common;

public abstract class Entity
{
    public Id Id { get; init; }

    protected Entity()
    {
        Id = Id.Empty;
    }

    protected Entity(Id id)
    {
        Id = id;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (GetType() != other.GetType())
            return false;

        if (Id.IsEmpty || other.Id.IsEmpty)
            return false;

        return Id == other.Id;
    }

    public static bool operator ==(Entity? left, Entity? right)
    {
        if (left is null && right is null)
            return true;
        if (left is null || right is null)
            return false;
        return left.Equals(right);
    }

    public static bool operator !=(Entity? left, Entity? right) => !(left == right);

    public override int GetHashCode() => Id.GetHashCode();
}
