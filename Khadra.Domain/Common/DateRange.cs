using CSharpFunctionalExtensions;

namespace Khadra.Domain.Common;

// Half-open [Start, End) period, used for rental periods and availability checks.
public sealed class DateRange : ValueObject
{
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }

    private DateRange(DateTimeOffset start, DateTimeOffset end)
    {
        Start = start;
        End = end;
    }

    public static Result<DateRange, Error> Create(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
            return Error.Validation("period.end_before_start", "The end of the period must be after its start.");

        return new DateRange(start, end);
    }

    public TimeSpan Duration => End - Start;

    // Rental days are billed in whole days, rounded up.
    public int WholeDays => (int)Math.Ceiling(Duration.TotalDays);

    public bool Overlaps(DateRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Start < other.End && other.Start < End;
    }

    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < End;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Start;
        yield return End;
    }
}
