namespace Khadra.Domain.Common;

/// <summary>
/// Raised by the unit of work when the database refuses a write because something else already holds
/// the thing being claimed — an exclusion constraint violation, SQLSTATE 23P01.
/// </summary>
/// <remarks>
/// This is the floor under every check-then-act in application code. A handler asks "is this car
/// free?", the answer is yes, and between the question and the insert somebody else books it. Only
/// the database can settle that, and this is how it says so.
///
/// The constraint's NAME travels with it because the exception is general and the meaning is not:
/// "bookings_one_hold_per_vehicle" is a car being double-booked, and a future constraint will mean
/// something else. A handler that expects a particular race checks the name and translates it into
/// the refusal its own callers understand; anything unrecognised stays an exception and becomes a
/// 409 with a generic code, which is the honest answer for a conflict nobody anticipated.
/// </remarks>
public sealed class ExclusiveHoldConflictException : Exception
{
    public string? ConstraintName { get; }

    public ExclusiveHoldConflictException()
    {
    }

    public ExclusiveHoldConflictException(string message) : base(message)
    {
    }

    public ExclusiveHoldConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ExclusiveHoldConflictException(string message, string? constraintName, Exception innerException)
        : base(message, innerException) => ConstraintName = constraintName;

    /// <summary>The constraint that stops two bookings holding one vehicle over the same dates.</summary>
    public const string VehicleHoldConstraint = "bookings_one_hold_per_vehicle";

    public bool IsVehicleHold =>
        string.Equals(ConstraintName, VehicleHoldConstraint, StringComparison.Ordinal);
}
