using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Common;

/// <summary>
/// Which failures this process has already reported (pre-launch item 236).
/// </summary>
/// <remarks>
/// <para>
/// The settlement sweeps run every minute and handle each booking on its own, so one that fails the same way every
/// pass no longer stops the others — but it was logged at Error once a minute for as long as it stayed broken, which
/// buries the next new problem under the same old line. A failure is now reported at Error the FIRST time this process
/// meets it, and at Debug after that, until the process restarts and reports it again. The way
/// <c>BookingSettlementService</c> already treats a PDF it cannot draw.
/// </para>
/// <para>
/// Keyed by what failed and how (an event and a booking reference), never by the message: the same booking failing a
/// DIFFERENT way is a new failure. A singleton, because the handlers that ask are created per pass.
/// </para>
/// </remarks>
public sealed class RepeatedFailureLog
{
    // Bounded so a sweep over many distinct failures cannot grow it without limit; past the cap everything is "new",
    // which errs towards saying too much rather than too little.
    private const int Capacity = 10_000;

    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.Ordinal);

    /// <summary>Error the first time this process meets <paramref name="key"/>; Debug every time after.</summary>
    public LogLevel LevelFor(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (_reported.ContainsKey(key))
            return LogLevel.Debug;
        if (_reported.Count < Capacity)
            _reported.TryAdd(key, 0);
        return LogLevel.Error;
    }

    /// <summary>Forgets a failure that has stopped happening, so a recurrence is reported again.</summary>
    public void Forget(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        _reported.TryRemove(key, out _);
    }
}
