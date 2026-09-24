using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Payments.Dtos;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.Payments.GetPaymentOptions;

/// <summary>The customer's payment picture for one of their bookings: figures, paid so far, both options.</summary>
public sealed record GetPaymentOptionsQuery(Id CustomerUserId, Id BookingId) : IQuery<Result<PaymentOptionsDto, Error>>;

public sealed class GetPaymentOptionsHandler(
    IBookingRepository bookings,
    BookingPaymentAvailability availability)
    : IRequestHandler<GetPaymentOptionsQuery, Result<PaymentOptionsDto, Error>>
{
    public async Task<Result<PaymentOptionsDto, Error>> Handle(GetPaymentOptionsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var booking = await bookings.GetByIdAsync(request.BookingId, cancellationToken);
        // Someone else's booking reads exactly like a missing one, as on every customer endpoint.
        if (booking is null || booking.CustomerId != request.CustomerUserId)
            return BookingErrors.NotFound;

        return await availability.OptionsAsync(booking, cancellationToken);
    }
}
