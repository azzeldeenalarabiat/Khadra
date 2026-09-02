namespace Khadra.Domain.Common;

// Raised by the unit of work when an optimistic-concurrency check fails (the row changed under us).
// Handlers that expect races (refresh-token rotation) catch it and translate to a Result failure.
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
    {
    }

    public ConcurrencyConflictException(string message) : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
