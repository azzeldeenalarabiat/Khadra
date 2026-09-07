using Khadra.Application.Common;
using Khadra.Domain.Common;

namespace Khadra.Application.IdentityAccess.ReadModels;

/// <summary>One row of the customer list, as the platform sees it.</summary>
public sealed record CustomerListItem(
    Guid UserId,
    string FullName,
    string Email,
    string Phone,
    string Status,
    bool IsEmailVerified,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    // The two facts an admin judges a customer on before reading further.
    int BookingCount,
    int DocumentCount,
    bool HasCompleteRenterDocuments);

/// <summary>
/// One customer in full, for the profile screen.
/// </summary>
/// <remarks>
/// The documents are described, never linked. Spec 7 keeps identity papers private and
/// <c>CustomerDocument</c> scopes viewing to the customer themselves and a dealer with an active
/// request — an Admin is not named there, and spec 5.1's review mechanism is still undecided. So the
/// screen says what is on file and what state it is in, and mints no signed URL for a passport.
/// </remarks>
public sealed record CustomerProfile(
    Guid UserId,
    string FullName,
    string Email,
    string Phone,
    string Status,
    string? SuspensionReason,
    bool IsEmailVerified,
    DateTimeOffset? EmailVerifiedAt,
    DateOnly? DateOfBirth,
    bool IsForeignNational,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? PasswordChangedAt,
    bool HasCompleteRenterDocuments,
    IReadOnlyList<CustomerDocumentSummary> Documents,
    CustomerBookingTotals Bookings);

/// <summary>What is on file, without exposing the file.</summary>
public sealed record CustomerDocumentSummary(
    Guid DocumentId,
    string Type,
    string Status,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    string? ReviewNote);

/// <summary>The customer's history with the platform, counted by the database.</summary>
public sealed record CustomerBookingTotals(
    int Total,
    int Completed,
    int Cancelled,
    int NoShow,
    int Live);

public sealed record CustomerListFilter(string? Status, string? Search, bool? UnverifiedOnly);

public interface ICustomerAdminReader
{
    Task<PagedResult<CustomerListItem>> ListAsync(
        CustomerListFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Null when there is no such customer — or the user is not a customer at all.</summary>
    Task<CustomerProfile?> GetAsync(Id userId, CancellationToken cancellationToken = default);

    /// <summary>How many customers sit in each state the list can filter to.</summary>
    Task<CustomerCountsView> CountsAsync(CancellationToken cancellationToken = default);
}

public sealed record CustomerCountsView(int Total, int Active, int Suspended, int Unverified);

/// <summary>One administrator, as the Admin users screen lists them.</summary>
/// <remarks>
/// "Invited" is not a status on <c>User</c>: it is an active account whose email has never been
/// verified, which is exactly what an unaccepted invitation looks like. Deriving it here keeps the
/// domain honest — there is no third state to persist — while the screen can still tell an
/// administrator who has never arrived from one who is working.
/// </remarks>
public sealed record AdminUserListItem(
    Guid UserId,
    string FullName,
    string Email,
    string Phone,
    string Status,
    bool IsEmailVerified,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    string? SuspensionReason,
    /// <summary>How many entries in the append-only trail are attributed to them.</summary>
    int AuditedActions);

public interface IAdminUserReader
{
    Task<IReadOnlyList<AdminUserListItem>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// One sign-in session: a whole refresh-token family, not a single token.
/// </summary>
/// <remarks>
/// A family is what a person would call "a device I signed in on": the first token is issued at
/// sign-in and every refresh replaces it inside the same family. Listing rows would show the same
/// device a dozen times, once per refresh.
/// </remarks>
public sealed record SessionSummary(
    Guid FamilyId,
    DateTimeOffset SignedInAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string? CreatedByIp,
    string? UserAgent,
    bool IsActive);

public interface ISessionReader
{
    Task<IReadOnlyList<SessionSummary>> ListForUserAsync(Id userId, CancellationToken cancellationToken = default);

    /// <summary>Whether this family belongs to this user, before anything is revoked by id.</summary>
    Task<bool> BelongsToUserAsync(Id userId, Guid familyId, CancellationToken cancellationToken = default);
}
