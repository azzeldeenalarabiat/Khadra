using CSharpFunctionalExtensions;
using Khadra.Application.Bookings;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Payments.Dtos;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using MediatR;

namespace Khadra.Application.Payments.OpenCheckout;

/// <summary>
/// The customer wants to pay the deposit on their own approved booking.
/// </summary>
/// <remarks>
/// Keyed on the BOOKING, never on an amount: a request that named its own figure would be a request
/// to choose what a rental costs. The amount comes from the booking's frozen pricing.
/// </remarks>
public sealed record OpenDepositCheckoutCommand(Id CustomerUserId, Id BookingId)
    : ICommand<Result<PaymentDto, Error>>;

/// <summary>
/// Opens, resumes or replaces the customer's checkout for a booking's deposit.
/// </summary>
/// <remarks>
/// <para>
/// The order is the point, and each step earns its place:
/// </para>
/// <list type="number">
/// <item>
/// <b>Is there a provider at all</b> — asked FIRST, before a single row is read. A platform with no
/// provider must answer 503 and touch nothing; opening a payment row that can never be paid would
/// leave a booking holding a live attempt that blocks the next one.
/// </item>
/// <item>
/// <b>Is this booking mine, and is it still awaiting payment</b> — through
/// <see cref="BookingDepositSettlement.DepositDue"/>, which is the ONE place the payment deadline is
/// enforced. Everything downstream of a capture is deliberately deadline-blind.
/// </item>
/// <item>
/// <b>Is there already a live attempt</b> — a usable one is returned unchanged, so a customer who
/// taps twice, or reopens the app, lands back on the same card form rather than orphaning a session
/// the provider still considers open.
/// </item>
/// <item>
/// <b>Insert, then call the provider</b> — in that order, and saved in between. The row's own id is
/// the idempotency key, so a crash after the provider is called but before its answer is stored
/// leaves an <see cref="PaymentStatus.Initiated"/> row that the next attempt resumes with the same
/// key. Calling the provider first would risk a charge with nothing on this side pointing at it.
/// </item>
/// </list>
/// <para>
/// The provider call sits BETWEEN two saves and inside no transaction. Holding a database
/// transaction open across a third-party network call is how a slow provider becomes a database
/// outage.
/// </para>
/// </remarks>
public sealed class OpenDepositCheckoutHandler(
    IBookingRepository bookings,
    IPaymentRepository payments,
    IUserRepository users,
    IPaymentProvider provider,
    IPaymentSettings settings,
    IClock clock,
    IUnitOfWork unitOfWork) : IRequestHandler<OpenDepositCheckoutCommand, Result<PaymentDto, Error>>
{
    public async Task<Result<PaymentDto, Error>> Handle(
        OpenDepositCheckoutCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Before any read. See the remarks: a platform that cannot take money must not create a row
        // that says it is trying to.
        if (!provider.IsConfigured)
            return PaymentErrors.ProviderUnavailable;

        var booking = await bookings.GetByIdAsync(request.BookingId, cancellationToken);
        // A booking that is not this customer's is NOT FOUND rather than forbidden, matching every
        // other customer-scoped endpoint: telling a stranger that a booking exists is itself a leak.
        if (booking is null || booking.CustomerId != request.CustomerUserId)
            return BookingErrors.NotFound;

        var now = clock.UtcNow;
        var due = BookingDepositSettlement.DepositDue(booking, now);
        if (due.IsFailure)
            return due.Error;

        var live = await payments.GetLiveForBookingAsync(request.BookingId, cancellationToken);
        if (live is not null)
        {
            // Usable and already carrying a session: hand back the same one. Two sessions for one
            // booking is how a customer pays twice.
            if (live.IsUsable(now) && live.Status == PaymentStatus.Pending)
                return PaymentDto.From(live);

            // Usable but never got an answer from the provider: resume it, with the SAME key, so the
            // provider returns the session it already made rather than making a second.
            if (live.IsUsable(now) && live.Status == PaymentStatus.Initiated)
                return await AskProviderAsync(live, booking, due.Value, cancellationToken);

            // Past its own expiry. Retire it in the same save as the replacement, so there is never a
            // moment with two live attempts for the partial unique index to refuse.
            var superseded = live.Fail("superseded", now);
            if (superseded.IsFailure)
                return superseded.Error;
        }

        var expiresAt = ExpiryFor(booking, now);
        if (expiresAt <= now)
            return BookingErrors.NotAwaitingPayment;

        var payment = Payment.Open(
            booking.Id,
            booking.CustomerId,
            due.Value,
            provider.Name,
            expiresAt,
            now);
        payments.Add(payment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await AskProviderAsync(payment, booking, due.Value, cancellationToken);
    }

    /// <summary>
    /// Asks the provider for a session and stores its answer.
    /// </summary>
    /// <remarks>
    /// A refusal leaves the row <see cref="PaymentStatus.Initiated"/> deliberately: it is still
    /// usable, so the customer's next tap resumes it with the same idempotency key instead of
    /// stacking another dead attempt behind it.
    /// </remarks>
    private async Task<Result<PaymentDto, Error>> AskProviderAsync(
        Payment payment,
        Booking booking,
        Money amount,
        CancellationToken cancellationToken)
    {
        var customer = await users.GetByIdAsync(booking.CustomerId, cancellationToken);
        if (customer is null)
            return BookingErrors.NotFound;

        var session = await provider.CreateCheckoutAsync(
            new CheckoutRequest(
                payment.Id,
                amount,
                booking.Reference.Value,
                customer.Email.Value,
                payment.ExpiresAt,
                settings.ReturnUrlFor(booking.Id)),
            cancellationToken);
        if (session.IsFailure)
            return session.Error;

        var attached = payment.AttachProviderSession(session.Value.ProviderReference, session.Value.CheckoutUrl);
        if (attached.IsFailure)
            return attached.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PaymentDto.From(payment);
    }

    /// <summary>
    /// When the checkout session should die: the configured length, but never so late that a capture
    /// could land after the booking stopped being able to take it.
    /// </summary>
    /// <remarks>
    /// The margin is what turns the sharpest race in the feature from a refund into a refusal. A
    /// customer told "too late" at the card form has lost nothing; one whose card was charged three
    /// seconds after their deadline has to be given their money back, and a refund needs a provider
    /// that may itself be down. Closing the door early is the cheap half of the fix; the webhook
    /// still handles a late capture correctly, because it must.
    /// </remarks>
    private DateTimeOffset ExpiryFor(Booking booking, DateTimeOffset now)
    {
        var ownLimit = now.Add(settings.CheckoutSessionLifetime);
        if (booking.PaymentDeadline is not { } deadline)
            return ownLimit;

        var deadlineLimit = deadline.Subtract(settings.CheckoutClosesBeforeDeadline);
        return ownLimit < deadlineLimit ? ownLimit : deadlineLimit;
    }
}
