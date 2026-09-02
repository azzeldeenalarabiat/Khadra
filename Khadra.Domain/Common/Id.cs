namespace Khadra.Domain.Common;

// Strongly-typed identity for every entity. UUIDv7 keeps PostgreSQL B-tree inserts sequential.
public readonly record struct Id(Guid Value)
{
    public static readonly Id Empty = new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static Id New() => new(Guid.CreateVersion7());

    public static Id From(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(Id id) => id.Value;

    public static implicit operator Id(Guid value) => From(value);
}
