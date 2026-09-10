namespace Khadra.Domain.Common;

/// <summary>
/// A record that may only ever be inserted: never updated, never deleted.
/// </summary>
/// <remarks>
/// <para>
/// A marker, deliberately with no members. The enforcement lives in
/// <c>KhadraDbContext.SaveChangesAsync</c>, which refuses to persist a modified or deleted entity
/// carrying this interface, and in a database trigger per table for anything that bypasses the
/// application entirely.
/// </para>
/// <para>
/// It exists because the guard used to name <c>AuditEntry</c> in its own type argument, so the second
/// append-only table on the platform would have been append-only in intention and mutable in fact
/// until somebody noticed. A record that the application can quietly rewrite is not a record.
/// </para>
/// </remarks>
public interface IAppendOnly;
