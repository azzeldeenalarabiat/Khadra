using System.Reflection;

namespace Khadra.Domain.Common;

// Smart enum base. Status/type fields are modelled as sealed subclasses with static readonly
// instances so behaviour can live next to the value and persistence is by stable Name.
public abstract class Enumeration : IEquatable<Enumeration>, IComparable<Enumeration>
{
    public int Id { get; }
    public string Name { get; }

    protected Enumeration(int id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
    }

    public static IReadOnlyList<T> GetAll<T>() where T : Enumeration =>
        typeof(T)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(field => field.GetValue(null))
            .OfType<T>()
            .ToList();

    public static T FromName<T>(string name) where T : Enumeration =>
        GetAll<T>().SingleOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal))
        ?? throw new DomainException($"'{name}' is not a valid {typeof(T).Name}.");

    public static T FromId<T>(int id) where T : Enumeration =>
        GetAll<T>().SingleOrDefault(item => item.Id == id)
        ?? throw new DomainException($"{id} is not a valid {typeof(T).Name}.");

    public bool Equals(Enumeration? other) =>
        other is not null && GetType() == other.GetType() && Id == other.Id;

    public override bool Equals(object? obj) => obj is Enumeration other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public int CompareTo(Enumeration? other) => other is null ? 1 : Id.CompareTo(other.Id);

    public override string ToString() => Name;

    public static bool operator ==(Enumeration? left, Enumeration? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Enumeration? left, Enumeration? right) => !(left == right);

    public static bool operator <(Enumeration left, Enumeration right) => left.CompareTo(right) < 0;

    public static bool operator <=(Enumeration left, Enumeration right) => left.CompareTo(right) <= 0;

    public static bool operator >(Enumeration left, Enumeration right) => left.CompareTo(right) > 0;

    public static bool operator >=(Enumeration left, Enumeration right) => left.CompareTo(right) >= 0;
}
