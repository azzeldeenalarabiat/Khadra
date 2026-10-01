using System.Data.Common;
using System.Net.Sockets;

namespace Khadra.Infrastructure.Persistence;

/// <summary>
/// Whether an exception means the database could not be read: refused, unreachable, timed out, or not answering the
/// question as asked (pre-launch item 221).
/// </summary>
/// <remarks>
/// <para>
/// <c>catch (DbException)</c> is not enough, and that is the whole item. EF Core's execution strategy hands a
/// TRANSIENT failure — a refused connection, a socket error, a timeout — to the caller wrapped in an
/// <see cref="InvalidOperationException"/> ("…likely due to a transient failure…"), and keeps the provider's
/// exception as the inner one. A non-transient failure — a missing table, a refused login — stays a
/// <see cref="DbException"/>. So the startup checks caught a database that answered wrongly and crashed on one that
/// did not answer at all, which is exactly the outage they were meant to ride out.
/// </para>
/// <para>
/// This walks the whole chain instead. It never matches an <see cref="InvalidOperationException"/> on its own: the
/// payments guard's own refusal is one, and must stay fatal.
/// </para>
/// <para>
/// <b>Log <see cref="Reason"/>, never the outer message.</b> For a refused connection that is EF's sentence advising
/// <c>EnableRetryOnFailure</c> — the wrong fix, in the one line an operator reads first — while the database's own
/// words ("Failed to connect to …", "42P01: relation … does not exist") sit one level down.
/// </para>
/// </remarks>
public static class DatabaseUnreadable
{
    public static bool IsCauseOf(Exception? exception) => CauseOf(exception) is not null;

    /// <summary>The first exception in the chain that comes from the database or the network, or null.</summary>
    public static Exception? CauseOf(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException or TimeoutException or SocketException)
                return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (CauseOf(inner) is { } found)
                        return found;
                }
            }
        }

        return null;
    }

    /// <summary>What to log about an unreadable database: its own words, or the exception's when there are none.</summary>
    public static string Reason(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return (CauseOf(exception) ?? exception).Message;
    }
}
