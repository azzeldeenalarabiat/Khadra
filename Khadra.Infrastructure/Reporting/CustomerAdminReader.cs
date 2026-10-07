using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class CustomerAdminReader(KhadraDbContext context) : ICustomerAdminReader
{
    public async Task<PagedResult<CustomerListItem>> ListAsync(
        CustomerListFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var query = Customers(filter.Search);

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = Enumeration.GetAll<UserStatus>()
                .SingleOrDefault(candidate =>
                    string.Equals(candidate.Name, filter.Status, StringComparison.OrdinalIgnoreCase));
            // An unrecognised status filters everything out rather than being ignored: returning the
            // unfiltered list would look like the filter worked and matched everyone.
            if (status is null)
                return PagedResult.Empty<CustomerListItem>(page.Page, page.PageSize);

            query = query.Where(user => user.Status == status);
        }

        if (filter.UnverifiedOnly == true)
            query = query.Where(user => !user.IsEmailVerified);

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult.Empty<CustomerListItem>(page.Page, page.PageSize);

        var rows = await query
            // The documents come WITH each customer, because "complete to rent" is the aggregate's own rule
            // (User.MissingRenterDocumentTypes) and is asked of the loaded customer below, never restated in SQL. It
            // used to be asked inside the projection, of a customer EF built with no documents, so the list called
            // every customer incomplete (E2E F86, Wave 4).
            .AsNoTracking()
            .Include(user => user.Documents)
            .AsSplitQuery()
            // Newest first: the people who just arrived are the ones an admin has not seen.
            .OrderByDescending(user => user.CreatedAt)
            // CreatedAt is not unique — the seeder writes many in a second — and a non-total order
            // lets a page boundary drop a customer or show them twice.
            .ThenBy(user => user.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(user => new
            {
                User = user,
                // Correlated rather than joined: Bookings is another bounded context, so there is no
                // navigation property from User to Booking and there deliberately never will be.
                Bookings = context.Bookings.Count(booking => booking.CustomerId == user.Id),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new CustomerListItem(
                row.User.Id.Value,
                row.User.Name.Value,
                row.User.Email.Value,
                row.User.Phone.Value,
                row.User.Status.Name,
                row.User.IsEmailVerified,
                row.User.LastLoginAt,
                row.User.CreatedAt,
                row.Bookings,
                row.User.Documents.Count,
                row.User.HasCompleteRenterDocuments))
            .ToList();

        return new PagedResult<CustomerListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<CustomerProfile?> GetAsync(Id userId, CancellationToken cancellationToken = default)
    {
        var customer = UserRole.Customer;
        var user = await context.Users
            .Include(candidate => candidate.Documents)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == userId && candidate.Role == customer,
                cancellationToken);

        if (user is null)
            return null;

        var completed = BookingStatus.Completed;
        var cancelled = BookingStatus.Cancelled;
        var noShow = BookingStatus.NoShow;

        // "Live" is a booking that has not reached a terminal state. Asked for as a list of captured
        // statuses, NOT as `!booking.Status.IsTerminal`: IsTerminal is a C# property on the smart
        // enum with no column behind it, so EF cannot translate it and the whole query throws. The
        // list is derived from IsTerminal itself, so a status added to the enum is classified once,
        // in the domain, rather than needing this to be remembered.
        var live = Enumeration.GetAll<BookingStatus>()
            .Where(status => !status.IsTerminal)
            .ToList();

        var bookings = context.Bookings.Where(booking => booking.CustomerId == user.Id);
        var totals = new CustomerBookingTotals(
            await bookings.CountAsync(cancellationToken),
            await bookings.CountAsync(booking => booking.Status == completed, cancellationToken),
            await bookings.CountAsync(booking => booking.Status == cancelled, cancellationToken),
            await bookings.CountAsync(booking => booking.Status == noShow, cancellationToken),
            await bookings.CountAsync(booking => live.Contains(booking.Status), cancellationToken));

        return new CustomerProfile(
            user.Id.Value,
            user.Name.Value,
            user.Email.Value,
            user.Phone.Value,
            user.Status.Name,
            user.SuspensionReason,
            user.IsEmailVerified,
            user.EmailVerifiedAt,
            user.DateOfBirth,
            user.IsForeignNational,
            user.CreatedAt,
            user.LastLoginAt,
            user.PasswordChangedAt,
            user.HasCompleteRenterDocuments,
            user.Documents
                .OrderBy(document => document.Type.Id)
                .Select(document => new CustomerDocumentSummary(
                    document.Id.Value,
                    document.Type.Name,
                    document.Status.Name,
                    document.ContentType,
                    document.SizeBytes,
                    document.UploadedAt,
                    document.ReviewNote))
                .ToList(),
            totals);
    }

    public async Task<CustomerCountsView> CountsAsync(CancellationToken cancellationToken = default)
    {
        var all = Customers(null);
        var suspended = UserStatus.Suspended;
        var active = UserStatus.Active;

        return new CustomerCountsView(
            await all.CountAsync(cancellationToken),
            await all.CountAsync(user => user.Status == active, cancellationToken),
            await all.CountAsync(user => user.Status == suspended, cancellationToken),
            await all.CountAsync(user => !user.IsEmailVerified, cancellationToken));
    }

    /// <summary>
    /// Customers only, optionally narrowed by a search.
    /// </summary>
    /// <remarks>
    /// The search goes through FromSql rather than LINQ because Name, Email and Phone are value
    /// objects stored through a converter: EF has a PersonName in the model and a varchar in the
    /// database, and there is no translation for ILIKE across that boundary. Naming the columns is
    /// the honest way to say what this actually does. The soft-delete filter still applies, so a
    /// deleted account is out of both branches.
    /// </remarks>
    private IQueryable<User> Customers(string? search)
    {
        var customer = UserRole.Customer;
        if (string.IsNullOrWhiteSpace(search))
            return context.Users.Where(user => user.Role == customer);

        var pattern = $"%{search.Trim()}%";
        return context.Users
            .FromSql(
                $"SELECT * FROM users WHERE name ILIKE {pattern} OR email ILIKE {pattern} OR phone ILIKE {pattern}")
            .Where(user => user.Role == customer);
    }
}
