using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

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

    // The subject first, so the (subject_user_id, occurred_at, id) index narrows it to one person's handful of rows.
    public Task<bool> AdministratorHasViewedAsync(
        Id administratorUserId,
        Id subjectUserId,
        Id documentId,
        DateTimeOffset documentUploadedAt,
        CancellationToken cancellationToken = default)
    {
        var viewed = DocumentAccessAction.Viewed;
        var admin = UserRole.Admin;
        return context.DocumentAccessEntries
            .AsNoTracking()
            .AnyAsync(
                entry => entry.SubjectUserId == subjectUserId
                    && entry.DocumentId == documentId
                    && entry.ActorUserId == administratorUserId
                    && entry.ActorRole == admin
                    && entry.Action == viewed
                    && entry.DocumentUploadedAt == documentUploadedAt,
                cancellationToken);
    }
}
