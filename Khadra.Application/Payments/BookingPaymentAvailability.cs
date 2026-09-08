using Khadra.Application.Bookings;
using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Common.Ports;
using Khadra.Application.Payments.Dtos;
using Khadra.Domain.Bookings;
using Khadra.Domain.Payments.Repositories;

namespace Khadra.Application.Payments;

/// <summary>
/// Answers "can this customer pay their deposit right now", and says why not when they cannot.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the answer is not on the booking. Three separate facts decide it: the booking's
/// own status and deadline, whether an attempt is already open, and whether this platform has a
/// payment provider at all. The last one is not a property of any rental and never will be, so no
/// client could work the answer out from a <c>BookingDto</c> however much of it it was given.
/// </para>
/// <para>
/// <see cref="PaymentAvailabilityDto.UnavailableReason"/> is what makes the screen honest. "Your
/// window closed" and "this platform cannot take cards yet" are different facts with different
/// remedies, and a customer shown the wrong one either gives up or complains about the wrong thing.
/// The codes are the platform's own error codes, so a client renders them with the same table it
/// already uses for a refused request.
/// </para>
/// </remarks>
public sealed class BookingPaymentAvailability(
    IPaymentProvider provider,
    IPaymentRepository payments,
    IClock clock)
{
    public async Task<PaymentAvailabilityDto> ForAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        var now = clock.UtcNow;
        var due = BookingDepositSettlement.DepositDue(booking, now);
        var amount = due.IsSuccess ? MoneyDto.From(due.Value) : null;

        // The attempt is reported whatever the verdict: a customer whose window closed while a
        // checkout was open still needs to see that it exists, and an orphaned capture is easier to
        // ask about when the screen admits an attempt was made.
        var live = await payments.GetLiveForBookingAsync(booking.Id, cancellationToken);
        var attempt = live is null ? null : PaymentDto.From(live);

        if (due.IsFailure)
            return new PaymentAvailabilityDto(false, due.Error.Code, null, booking.PaymentDeadline, attempt);

        if (!provider.IsConfigured)
        {
            // The amount and the deadline are still sent. The customer is owed the figure and the
            // date even though they cannot act on them yet -- item 69's whole point is that the app
            // tells the truth about this rather than showing a button that cannot work.
            return new PaymentAvailabilityDto(
                false,
                Domain.Payments.PaymentErrors.ProviderUnavailable.Code,
                amount,
                booking.PaymentDeadline,
                attempt);
        }

        return new PaymentAvailabilityDto(true, null, amount, booking.PaymentDeadline, attempt);
    }
}
