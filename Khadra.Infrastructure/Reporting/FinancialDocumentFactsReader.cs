using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.Payments.Repositories;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>
/// What a financial document is composed from (payments Phase 5), read as COMMITTED.
/// </summary>
/// <remarks>
/// <para>
/// The booking comes from <see cref="BookingRepository"/> itself, NOT the clock-settling repository every
/// handler uses (owner, 2026-09-27: documents are composed from saved records only). That repository
/// applies a lapse in memory and stamps the ending with the current instant — the right answer for a live
/// screen, and the wrong one for a record: the stamp would move on every pass, and a document would freeze
/// an ending nobody saved. The settlement pass commits every lapse before it issues a document.
/// </para>
/// <para>
/// Nothing read here is modified. The issuing scope saves only the document and its hold.
/// </para>
/// </remarks>
internal sealed class FinancialDocumentFactsReader(
    BookingRepository bookings,
    IPaymentRepository payments,
    IDisputeTicketRepository tickets,
    KhadraDbContext context) : IFinancialDocumentFactsReader
{
    public async Task<BookingMoneyFacts?> BookingAsync(Id bookingId, CancellationToken cancellationToken = default)
    {
        // One after another: the repositories share this scope's context.
        var booking = await bookings.GetByIdAsync(bookingId, cancellationToken);
        if (booking is null)
            return null;

        var own = await payments.ListForBookingAsync(bookingId, cancellationToken);
        var resolved = await tickets.ListResolvedForBookingAsync(bookingId, cancellationToken);
        var live = await tickets.HasLiveTicketAsync(bookingId, cancellationToken);
        return new BookingMoneyFacts(booking, own, resolved, live);
    }

    public async Task<DocumentParties?> PartiesAsync(
        Id customerId,
        Id dealerId,
        Id vehicleId,
        CancellationToken cancellationToken = default)
    {
        // Past the soft-delete filter, deliberately (CLAUDE.md: never without a reason). A document names the
        // customer, the office that received the money and the car it was for, and it must still name them
        // after any of the three is deleted: deletion must not blank a financial record, nor stop one issuing.
        var customer = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(user => user.Id == customerId)
            .Select(user => new { Name = user.Name.Value })
            .FirstOrDefaultAsync(cancellationToken);
        var dealer = await context.Dealers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(candidate => candidate.Id == dealerId)
            .Select(candidate => new
            {
                Name = candidate.BusinessName.Value,
                Registration = candidate.CommercialRegistration.Value,
                candidate.CityId,
                Area = candidate.Address == null ? null : candidate.Address.Area,
                Street = candidate.Address == null ? null : candidate.Address.Street,
            })
            .FirstOrDefaultAsync(cancellationToken);
        var vehicle = await context.Vehicles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(candidate => candidate.Id == vehicleId)
            .Select(candidate => new
            {
                candidate.Details.Make,
                candidate.Details.Model,
                candidate.Details.Year,
                Plate = candidate.PlateNumber.Value,
                candidate.CarTypeId,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (customer is null || dealer is null || vehicle is null)
            return null;

        // The lookups keep retired rows (IsActive), so a city or car type that has since been retired still names.
        var city = dealer.CityId is { } cityId
            ? await context.Cities
                .AsNoTracking()
                .Where(entry => entry.Id == cityId)
                .Select(entry => new { entry.NameEn, entry.NameAr })
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var carType = await context.CarTypes
            .AsNoTracking()
            .Where(entry => entry.Id == vehicle.CarTypeId)
            .Select(entry => new { entry.NameEn, entry.NameAr })
            .FirstOrDefaultAsync(cancellationToken);

        return new DocumentParties(
            new CustomerParty(customerId, customer.Name),
            new OfficeParty(
                dealerId,
                dealer.Name,
                dealer.Registration,
                city is null ? null : BilingualText.Of(city.NameEn, city.NameAr),
                string.IsNullOrWhiteSpace(dealer.Area) ? null : dealer.Area,
                string.IsNullOrWhiteSpace(dealer.Street) ? null : dealer.Street),
            new VehicleParty(
                vehicleId,
                vehicle.Make,
                vehicle.Model,
                vehicle.Year,
                vehicle.Plate,
                carType is null ? null : BilingualText.Of(carType.NameEn, carType.NameAr)));
    }
}
