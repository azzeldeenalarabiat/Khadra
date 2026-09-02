using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Dealers;

public sealed class DaySchedule : ValueObject
{
    public DayOfWeek Day { get; }
    public bool IsClosed { get; }
    public TimeOnly OpensAt { get; }
    public TimeOnly ClosesAt { get; }

    private DaySchedule(DayOfWeek day, bool isClosed, TimeOnly opensAt, TimeOnly closesAt)
    {
        Day = day;
        IsClosed = isClosed;
        OpensAt = opensAt;
        ClosesAt = closesAt;
    }

    public static DaySchedule Closed(DayOfWeek day) => new(day, isClosed: true, TimeOnly.MinValue, TimeOnly.MinValue);

    public static Result<DaySchedule, Error> Open(DayOfWeek day, TimeOnly opensAt, TimeOnly closesAt)
    {
        // Overnight opening (closing after midnight) is not supported: rental offices do not trade
        // through the night, and allowing it would make IsOpenAt ambiguous.
        if (closesAt <= opensAt)
            return DealerErrors.InvalidOperatingHours;

        return new DaySchedule(day, isClosed: false, opensAt, closesAt);
    }

    public bool IsOpenAt(TimeOnly localTime) => !IsClosed && localTime >= OpensAt && localTime < ClosesAt;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Day;
        yield return IsClosed;
        yield return OpensAt;
        yield return ClosesAt;
    }
}

// The dealer's weekly schedule. Times are LOCAL to the dealer; the domain never guesses a time zone,
// so callers convert to local time before asking. This keeps a Jordan-only launch honest without
// baking UTC+3 into the model.
public sealed class OperatingHours : ValueObject
{
    private readonly Dictionary<DayOfWeek, DaySchedule> _days;

    public IReadOnlyCollection<DaySchedule> Days => _days.Values.OrderBy(day => day.Day).ToList();

    private OperatingHours(Dictionary<DayOfWeek, DaySchedule> days)
    {
        _days = days;
    }

    public static Result<OperatingHours, Error> Create(IEnumerable<DaySchedule> schedules)
    {
        ArgumentNullException.ThrowIfNull(schedules);

        var byDay = new Dictionary<DayOfWeek, DaySchedule>();
        foreach (var schedule in schedules)
        {
            if (!byDay.TryAdd(schedule.Day, schedule))
                return DealerErrors.InvalidOperatingHours;
        }

        if (byDay.Count != 7)
            return DealerErrors.InvalidOperatingHours;

        return new OperatingHours(byDay);
    }

    // Convenience for the common case: the same hours every day of the week.
    public static Result<OperatingHours, Error> Uniform(TimeOnly opensAt, TimeOnly closesAt)
    {
        var schedules = new List<DaySchedule>();
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var schedule = DaySchedule.Open(day, opensAt, closesAt);
            if (schedule.IsFailure)
                return schedule.Error;
            schedules.Add(schedule.Value);
        }

        return Create(schedules);
    }

    public static OperatingHours AlwaysClosed() =>
        new(Enum.GetValues<DayOfWeek>().ToDictionary(day => day, DaySchedule.Closed));

    public DaySchedule For(DayOfWeek day) => _days[day];

    public bool IsOpenAt(DayOfWeek day, TimeOnly localTime) => _days[day].IsOpenAt(localTime);

    public bool IsClosedAllWeek => _days.Values.All(day => day.IsClosed);

    protected override IEnumerable<object?> GetEqualityComponents() => Days;
}
