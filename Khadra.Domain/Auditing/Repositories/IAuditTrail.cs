namespace Khadra.Domain.Auditing.Repositories;

// Write side of the audit trail.
//
// Record() only stages the entry; the calling handler's IUnitOfWork.SaveChangesAsync commits it in
// the SAME transaction as the action being recorded. That is the point: an audit write that could
// fail independently of the action would let a privileged change happen with no record of it.
public interface IAuditTrail
{
    void Record(AuditEntry entry);
}
