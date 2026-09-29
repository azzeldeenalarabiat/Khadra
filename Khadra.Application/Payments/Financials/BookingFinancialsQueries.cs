using CSharpFunctionalExtensions;
using Khadra.Application.Bookings;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Payables.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.Payments.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Payments.Financials;

/// <summary>
/// A booking's financial state for someone who is a party to it: the customer's projection for the
/// customer, the office's for the office's owner and active employees. Anyone else is told the booking
/// does not exist, exactly as <c>GET /bookings/{id}</c> tells them.
/// </summary>
public sealed record GetBookingFinancialsQuery(Id UserId, Id BookingId) : IQuery<Result<BookingFinancialsDto, Error>>;

/// <summary>Any booking's financial state, as the administrator sees it: every attempt, every share.</summary>
public sealed record GetAnyBookingFinancialsQuery(Id BookingId) : IQuery<Result<BookingFinancialsDto, Error>>;

public sealed partial class BookingFinancialsHandlers(
    IBookingRepository bookings,
    IPaymentRepository payments,
    IDisputeTicketRepository tickets,
    IOfficeLedgerReader ledger,
    BookingPartyResolver parties,
    IClock clock,
    ILogger<BookingFinancialsHandlers> logger)
    : IRequestHandler<GetBookingFinancialsQuery, Result<BookingFinancialsDto, Error>>,
      IRequestHandler<GetAnyBookingFinancialsQuery, Result<BookingFinancialsDto, Error>>
{
    public async Task<Result<BookingFinancialsDto, Error>> Handle(
        GetBookingFinancialsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var booking = await bookings.GetByIdAsync(request.BookingId, cancellationToken);
        if (booking is null)
            return BookingErrors.NotFound;

        // The same answer for a stranger as for a booking that does not exist: the resolver's not_found.
        var party = await parties.ResolveAsync(booking, request.UserId, cancellationToken);
        if (party.IsFailure)
            return party.Error;

        return await AnswerAsync(booking, party.Value, cancellationToken);
    }

    public async Task<Result<BookingFinancialsDto, Error>> Handle(
        GetAnyBookingFinancialsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var booking = await bookings.GetByIdAsync(request.BookingId, cancellationToken);
        if (booking is null)
            return BookingErrors.NotFound;

        return await AnswerAsync(booking, BookingParty.Admin, cancellationToken);
    }

    /// <summary>
    /// Reads the booking's payments, disputes and — for the office and the administrator — what the payables ledger
    /// holds about it, one after another (they share the request's DbContext), and runs the calculator.
    /// Contradictions are logged for a human, never thrown: the answer is still served.
    /// </summary>
    private async Task<BookingFinancialsDto> AnswerAsync(Booking booking, BookingParty reader, CancellationToken cancellationToken)
    {
        var bookingPayments = await payments.ListForBookingAsync(booking.Id, cancellationToken);
        var resolved = await tickets.ListResolvedForBookingAsync(booking.Id, cancellationToken);
        var hasLiveDispute = await tickets.HasLiveTicketAsync(booking.Id, cancellationToken);
        // The customer is never shown the ledger, but the deposit it kept as a penalty is theirs to read.
        var entry = reader == BookingParty.Customer ? null : await ledger.ForBookingAsync(booking.Id, cancellationToken);
        var recorded = entry is null
            ? await ledger.RecordedAsync(booking.Id, cancellationToken)
            : entry.Payable is { } payable
                ? new RecordedPayable(payable.PayableId, payable.Outcome, Money.Create(payable.Commission, payable.Currency), payable.RecordedAt)
                : null;

        var financials = BookingFinancialsCalculator.Calculate(booking, bookingPayments, resolved, hasLiveDispute, clock.UtcNow, recorded);
        if (financials.NeedsReview)
            LogNeedsReview(logger, booking.Id.Value, string.Join(", ", financials.Issues));
        return BookingFinancialsDto.For(financials, reader, entry);
    }

    [LoggerMessage(
        2410,
        LogLevel.Warning,
        "Booking {BookingId}: its payment records contradict one another ({Issues}). The financial state is served as the records say; somebody needs to look.")]
    private static partial void LogNeedsReview(ILogger logger, Guid bookingId, string issues);
}
