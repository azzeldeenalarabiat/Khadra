using CSharpFunctionalExtensions;

namespace Khadra.Domain.Common;

// Half-open [Start, End) period, used for rental periods and availability checks.
//
// Both ends are normalised to UTC. Every instant in this system is stored as UTC — Npgsql refuses a
// non-zero offset on a `timestamptz` column outright — and until now that was true only by accident,
// because every period was built from `clock.UtcNow`. The customer app changes that: a phone in
// Amman submits `2026-09-10T09:00:00+03:00`, and without this the write would fail at SaveChanges
// with an error about offsets that says nothing about bookings. Normalising here keeps the same
// instant, and value equality was already instant-based, so nothing else shifts.
//
// There is deliberately no day count on this type. A rental is billed in CALENDAR days, which is a
// question about someone's local calendar, and the shared kernel has no time zone to answer it with.
// That count belongs to pricing: see RentalDays and BookingPricing.
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

        return new DateRange(start.ToUniversalTime(), end.ToUniversalTime());
    }

    public TimeSpan Duration => End - Start;

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
