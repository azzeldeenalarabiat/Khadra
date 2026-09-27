using System.Text.Json;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.Payments.AdminPayments;
using Khadra.Application.Payments.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Payments;

/// <summary>
/// The administrator's payments list, refunds queue and payment page, above their reader (payments
/// Phase 4b): filters are the domain's own words or refused, Amman days become a half-open range of
/// instants, and a payment's page is the SAME description its booking's financial state gives it.
/// </summary>
public sealed class AdminPaymentQueryTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly IPaymentAdminReader _reader = Substitute.For<IPaymentAdminReader>();
    private readonly IPaymentRepository _payments = Substitute.For<IPaymentRepository>();
    private readonly IReportingCalendar _calendar = Substitute.For<IReportingCalendar>();
    private readonly IFinancialDocumentReader _documents = Substitute.For<IFinancialDocumentReader>();

    public AdminPaymentQueryTests()
    {
        // Amman is UTC+3: a local day begins at 21:00 the evening before, in UTC.
        _calendar.StartOfDay(Arg.Any<DateOnly>()).Returns(call =>
            new DateTimeOffset(call.Arg<DateOnly>().ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(3)));
        _reader.ListPaymentsAsync(Arg.Any<AdminPaymentFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(PagedResult.Empty<AdminPaymentListItem>(1, 20));
        _reader.ListRefundsAsync(Arg.Any<AdminRefundFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(PagedResult.Empty<AdminRefundListItem>(1, 20));
        _documents.ListForPaymentAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FinancialDocumentRecord>>([]));
    }

    private AdminPaymentQueryHandlers Handlers() => new(_reader, _payments, _documents, _calendar);

    [Fact]
    public void Unknown_filter_words_and_a_backwards_range_are_refused()
    {
        var payments = new ListAdminPaymentsQueryValidator();
        var refunds = new ListAdminRefundsQueryValidator();
        var day = new DateOnly(2026, 9, 10);

        Assert.False(payments.Validate(new ListAdminPaymentsQuery("Teleported", null, null, null, null, null, null, null, null)).IsValid);
        Assert.False(payments.Validate(new ListAdminPaymentsQuery(null, "Tip", null, null, null, null, null, null, null)).IsValid);
        Assert.False(payments.Validate(new ListAdminPaymentsQuery(null, null, null, null, null, day, day.AddDays(-1), null, null)).IsValid);
        Assert.True(payments.Validate(new ListAdminPaymentsQuery("orphaned", "fullpayment", null, null, null, day, day, null, null)).IsValid);
        Assert.False(refunds.Validate(new ListAdminRefundsQuery("Lost", null, null, null, null, null, null)).IsValid);
        Assert.False(refunds.Validate(new ListAdminRefundsQuery(null, "Goodwill", null, null, null, null, null)).IsValid);
        Assert.True(refunds.Validate(new ListAdminRefundsQuery("failed", "DisputeResolution", null, null, null, null, null)).IsValid);
    }

    [Fact]
    public async Task The_list_asks_for_the_domains_own_values_and_whole_amman_days()
    {
        var dealerId = Guid.NewGuid();
        var from = new DateOnly(2026, 9, 10);
        var to = new DateOnly(2026, 9, 12);

        await Handlers().Handle(
            new ListAdminPaymentsQuery("orphaned", "Deposit", dealerId, null, "KH-ABC12345", from, to, 2, 50),
            CancellationToken.None);

        await _reader.Received().ListPaymentsAsync(
            Arg.Is<AdminPaymentFilter>(filter =>
                filter.Status == PaymentStatus.Orphaned &&
                filter.Purpose == PaymentPurpose.Deposit &&
                filter.DealerId == Id.From(dealerId) &&
                filter.CustomerId == null &&
                filter.Reference == "KH-ABC12345" &&
                // From the start of the first day to the start of the day AFTER the last: all of the 12th.
                filter.CreatedFrom == new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.FromHours(3)) &&
                filter.CreatedBefore == new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.FromHours(3))),
            Arg.Is<PageRequest>(page => page.Page == 2 && page.PageSize == 50),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_queue_with_no_status_asks_for_the_live_queue()
    {
        await Handlers().Handle(new ListAdminRefundsQuery(null, null, null, null, null, null, null), CancellationToken.None);

        await _reader.Received().ListRefundsAsync(
            Arg.Is<AdminRefundFilter>(filter => filter.Status == null && filter.Reason == null),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_filter_vocabulary_is_the_domains_own()
    {
        var vocabulary = (await Handlers().Handle(new GetPaymentVocabularyQuery(), CancellationToken.None)).Value;

        Assert.Equal(["Initiated", "Pending", "Failed", "Applied", "Orphaned"], vocabulary.PaymentStatuses);
        Assert.Equal(["Deposit", "FullPayment", "RemainingBalance"], vocabulary.PaymentPurposes);
        Assert.Equal(["Requested", "Sent", "Settled", "Failed"], vocabulary.RefundStatuses);
        Assert.Contains("DisputeWindowClosed", vocabulary.RefundReasons);
    }

    [Fact]
    public async Task A_payment_nobody_has_is_not_found()
    {
        var result = await Handlers().Handle(new GetAdminPaymentQuery(Id.New()), CancellationToken.None);

        Assert.Equal("payments.not_found", result.Error.Code);
    }

    [Fact]
    public async Task A_payments_page_is_its_financial_description_with_its_booking_and_provider_events_and_never_its_checkout_link()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        _payments.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);
        var link = new PaymentBookingLink(booking.Id.Value, booking.Reference.Value, "Confirmed", booking.DealerId.Value, "Petra Rentals", booking.CustomerId.Value, null);
        _reader.BookingLinkAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(link);
        _reader.ProviderEventsAsync(payment.Id, payment.Provider, payment.ProviderReference, Arg.Any<CancellationToken>())
            .Returns([new ProviderEventItem(Guid.NewGuid(), "evt_1", "Captured", "Acted", null, Now, "Payment")]);

        var result = await Handlers().Handle(new GetAdminPaymentQuery(payment.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var page = result.Value;
        Assert.Equal(payment.Id.Value, page.Payment.PaymentId);
        Assert.Equal("None", page.Payment.RefundProgress);
        // The administrator's projection: the fee and the provider's reference are on it.
        Assert.Equal(4.5m, page.Payment.ProcessingFee!.Amount);
        Assert.Equal(payment.ProviderReference, page.Payment.ProviderReference);
        Assert.Same(link, page.Booking);
        Assert.Single(page.ProviderEvents);

        var json = JsonSerializer.Serialize(page, Web);
        Assert.DoesNotContain("checkout", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider.test", json, StringComparison.OrdinalIgnoreCase);
    }
}
