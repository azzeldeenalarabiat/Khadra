using Khadra.Application.Common;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class DealerAdminReader(KhadraDbContext context) : IDealerAdminReader
{
    public async Task<PagedResult<DealerListItem>> ListAsync(
        DealerListFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        // The soft-delete filter on dealers applies to both branches, so removed applications are
        // already out either way.
        //
        // Search goes through FromSql rather than LINQ because BusinessName and CommercialRegistration
        // are value objects stored through a converter: EF has a BusinessName in the model and a
        // varchar in the database, and there is no translation for "ILIKE" across that boundary.
        // Naming the columns is the honest way to say what this actually does.
        var query = string.IsNullOrWhiteSpace(filter.Search)
            ? context.Dealers.AsQueryable()
            : SearchQuery(filter.Search.Trim());

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = Enumeration.GetAll<DealerVerificationStatus>()
                .SingleOrDefault(candidate =>
                    string.Equals(candidate.Name, filter.Status, StringComparison.OrdinalIgnoreCase));
            // An unrecognised status filters everything out rather than being ignored: silently
            // returning the unfiltered list would look like the filter worked and found everything.
            if (status is null)
                return PagedResult.Empty<DealerListItem>(page.Page, page.PageSize);

            query = query.Where(dealer => dealer.VerificationStatus == status);
        }

        if (filter.SuspendedOnly == true)
            query = query.Where(dealer => dealer.IsSuspended);

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult.Empty<DealerListItem>(page.Page, page.PageSize);

        var approved = DealerVerificationStatus.Approved;
        // Read once out here: it is a domain constant, not a column, and projecting it per row keeps
        // the "n of m" column honest without EF trying to translate the enumeration into SQL.
        var requiredDocuments = DealerDocumentType.Required.Count;

        // Captured as a local for the same reason `approved` is: EF translates a comparison against
        // a captured enumeration, but not a call to `VerificationStatus.IsAwaitingAdmin`.
        var pending = DealerVerificationStatus.PendingReview;

        var items = await query
            // Applications still owed a decision come first, closest to the deadline at the top;
            // everything settled follows, newest first.
            //
            // Ordering the whole list by ReviewDueAt alone read as "closest to the deadline" but was
            // not: ReviewDueAt is set on EVERY dealer at submission and never cleared, so a
            // dealership approved in January sorted above the one application actually waiting --
            // which sat thirteenth of fourteen, under twelve decisions taken months earlier. The
            // deadline only means anything while a decision is outstanding, so it only sorts those.
            .OrderByDescending(dealer => dealer.VerificationStatus == pending)
            .ThenBy(dealer => dealer.VerificationStatus == pending ? dealer.ReviewDueAt : DateTimeOffset.MaxValue)
            .ThenByDescending(dealer => dealer.CreatedAt)
            // A non-total order lets a page boundary drop or repeat a row, the same reason the audit
            // reader breaks its ties on the id.
            .ThenBy(dealer => dealer.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(dealer => new DealerListItem(
                dealer.Id.Value,
                dealer.BusinessName.Value,
                dealer.CommercialRegistration.Value,
                dealer.VerificationStatus.Name,
                dealer.IsSuspended,
                dealer.VerificationStatus == approved && !dealer.IsSuspended,
                dealer.SubmittedAt,
                dealer.ReviewDueAt,
                dealer.CreatedAt,
                dealer.Documents.Count,
                requiredDocuments,
                // Active staff, matching DealerProfileDto. Counting every row here would give the
                // list a different staff figure from the dealer's own page for the same dealership.
                dealer.Employees.Count(employee => employee.IsActive),
                // Correlated rather than joined: Fleet is another bounded context, so there is no
                // navigation property from Dealer to Vehicle and there deliberately never will be.
                // The vehicles' own soft-delete filter removes deleted listings from this count.
                context.Vehicles.Count(vehicle => vehicle.DealerId == dealer.Id),
                // Reviews have no table yet, so there is nothing to average. Null, never 0.0.
                null,
                0))
            .ToListAsync(cancellationToken);

        return new PagedResult<DealerListItem>(items, page.Page, page.PageSize, total);
    }

    private IQueryable<Dealer> SearchQuery(string term)
    {
        var pattern = $"%{term}%";
        return context.Dealers.FromSql(
            $"SELECT * FROM dealers WHERE business_name ILIKE {pattern} OR commercial_registration ILIKE {pattern}");
    }
}
