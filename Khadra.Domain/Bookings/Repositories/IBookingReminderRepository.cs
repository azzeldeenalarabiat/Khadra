namespace Khadra.Domain.Bookings.Repositories;

public interface IBookingReminderRepository
{
    /// <summary>Stages the record. The unique (booking, kind, anchor) key refuses a second one at save.</summary>
    Task AddAsync(BookingReminder reminder, CancellationToken cancellationToken = default);
}
