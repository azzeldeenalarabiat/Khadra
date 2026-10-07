namespace Khadra.Domain.Common;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // Runs `work` inside one database transaction; commits on success, rolls back on exception.
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

    // Forgets every change this unit has staged and every entity it is tracking, after a save the database refused
    // (Wave 4). EF keeps a refused save's mutations in its tracker, so a handler that must still record something
    // afterwards would otherwise write the refused state again. Whatever it records next is read afresh.
    void DiscardChanges();
}
