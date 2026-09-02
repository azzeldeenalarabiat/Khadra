using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Tests.Domain.Common;

public sealed class IdTests
{
    [Fact]
    public void New_ids_are_unique_and_not_empty()
    {
        var first = Id.New();
        var second = Id.New();

        Assert.False(first.IsEmpty);
        Assert.NotEqual(first, second);
        Assert.True(Id.Empty.IsEmpty);
    }

    [Fact]
    public void Converts_to_and_from_guid()
    {
        var guid = Guid.NewGuid();
        Id id = guid;
        Guid back = id;

        Assert.Equal(guid, back);
        Assert.Equal(Id.From(guid), id);
    }
}

public sealed class EnumerationTests
{
    [Fact]
    public void Resolves_by_name_and_id()
    {
        Assert.Same(UserRole.DealerOwner, Enumeration.FromName<UserRole>("DealerOwner"));
        Assert.Same(UserRole.Customer, Enumeration.FromId<UserRole>(4));
        Assert.Equal(4, Enumeration.GetAll<UserRole>().Count);
    }

    [Fact]
    public void Unknown_name_is_a_programming_error()
    {
        Assert.Throws<DomainException>(() => Enumeration.FromName<UserStatus>("Frozen"));
    }

    [Fact]
    public void Equality_is_by_type_and_id()
    {
        Assert.True(UserRole.Admin == Enumeration.FromName<UserRole>("Admin"));
        Assert.NotEqual<Enumeration>(UserRole.Admin, UserStatus.Active);
    }
}

public sealed class MoneyTests
{
    [Fact]
    public void Rounds_to_three_minor_units_and_keeps_currency()
    {
        var money = Money.Jod(12.3456m);

        Assert.Equal(12.346m, money.Amount);
        Assert.Equal("JOD", money.CurrencyCode);
    }

    [Fact]
    public void Adds_and_takes_percentages_in_the_same_currency()
    {
        var total = Money.Jod(100m);

        Assert.Equal(Money.Jod(120m), total.Add(Money.Jod(20m)));
        Assert.Equal(Money.Jod(20m), total.Percentage(20m));
        Assert.Equal(Money.Jod(80m), total.Subtract(Money.Jod(20m)));
    }

    [Fact]
    public void Rejects_negative_amounts_and_mixed_currencies()
    {
        Assert.Throws<DomainException>(() => Money.Jod(-1m));
        Assert.Throws<DomainException>(() => Money.Jod(1m).Add(Money.Create(1m, "USD")));
        Assert.Throws<DomainException>(() => Money.Jod(5m).Subtract(Money.Jod(10m)));
    }
}

public sealed class GeoPointTests
{
    [Fact]
    public void Rejects_out_of_range_coordinates()
    {
        Assert.True(GeoPoint.Create(91, 0).IsFailure);
        Assert.True(GeoPoint.Create(0, 181).IsFailure);
        Assert.True(GeoPoint.Create(double.NaN, 0).IsFailure);
    }

    [Fact]
    public void Computes_great_circle_distance()
    {
        var amman = GeoPoint.Create(31.9539, 35.9106).Value;
        var zarqa = GeoPoint.Create(32.0728, 36.0880).Value;

        var distance = amman.DistanceKmTo(zarqa);

        Assert.InRange(distance, 18, 25);
        Assert.Equal(0, amman.DistanceKmTo(amman), precision: 6);
    }
}

public sealed class DateRangeTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void End_must_be_after_start()
    {
        Assert.True(DateRange.Create(Start, Start).IsFailure);
        Assert.True(DateRange.Create(Start, Start.AddHours(-1)).IsFailure);
    }

    [Fact]
    public void Whole_days_round_up_and_overlap_is_half_open()
    {
        var rental = DateRange.Create(Start, Start.AddDays(2.5)).Value;
        var adjacent = DateRange.Create(Start.AddDays(2.5), Start.AddDays(4)).Value;
        var overlapping = DateRange.Create(Start.AddDays(1), Start.AddDays(3)).Value;

        Assert.Equal(3, rental.WholeDays);
        Assert.False(rental.Overlaps(adjacent));
        Assert.True(rental.Overlaps(overlapping));
    }
}
