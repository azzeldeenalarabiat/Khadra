namespace Khadra.Domain.Common;

// Thrown only for programming errors and impossible states (e.g. a factory called with an invalid
// value object that should already have been validated). Expected rule violations return Result.
public sealed class DomainException : Exception
{
    public DomainException()
    {
    }

    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
