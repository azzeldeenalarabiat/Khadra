using Microsoft.Extensions.Logging;

namespace Khadra.Tests.Support;

/// <summary>
/// Keeps what was logged, so a test can assert both what a log line SAYS and what it must never say.
/// </summary>
/// <remarks>
/// The second half is the point. Diagnostic logging around authentication is written to be read by
/// an operator and by nobody else, and the line between "useful" and "a copy of the credential" is
/// one careless interpolation wide. A handler that logs its reasoning is only safe while something
/// checks that the address, the token and the link are not in it — and nothing checks that by
/// reading the code, because the next person to add a line will not re-read this one.
/// </remarks>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(EventId Id, LogLevel Level, string Message)> Entries { get; } = [];

    /// <summary>Every rendered message, joined — for asserting that a secret appears in none of them.</summary>
    public string AllText => string.Join("\n", Entries.Select(entry => entry.Message));

    public bool Logged(int eventId) => Entries.Exists(entry => entry.Id.Id == eventId);

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Entries.Add((eventId, logLevel, formatter(state, exception)));
    }
}
