using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;

namespace Khadra.Infrastructure.Persistence.Repositories;

// Stages the entry on the same DbContext the caller is about to save, exactly as AuditTrail does.
//
// No SaveChanges here, for the same reason: the record and the disclosure it describes have to commit
// together or not at all, and committing is the calling handler's single SaveChangesAsync.
internal sealed class DocumentAccessLog(KhadraDbContext context) : IDocumentAccessLog
{
    public void Record(DocumentAccessEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        context.DocumentAccessEntries.Add(entry);
    }
}
