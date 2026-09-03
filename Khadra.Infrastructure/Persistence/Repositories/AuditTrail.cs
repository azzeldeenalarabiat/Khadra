using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;

namespace Khadra.Infrastructure.Persistence.Repositories;

// Stages the entry on the same DbContext the caller is about to save.
//
// There is deliberately no SaveChanges here. The whole guarantee of the audit trail is that the
// record and the action it describes commit together or not at all, so committing is the calling
// handler's single SaveChangesAsync, not a second write this class could do on its own.
internal sealed class AuditTrail(KhadraDbContext context) : IAuditTrail
{
    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        context.AuditEntries.Add(entry);
    }
}
