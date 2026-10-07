using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Application.Legal;
using Khadra.Application.Legal.ReadModels;
using Khadra.Application.PlatformSettings.AppConfig;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.GetCurrentUser;

/// <summary>
/// <c>GET /auth/me</c>: the signed-in person, and the legal texts in force they have still to accept (Wave 4, W4-8).
/// </summary>
/// <remarks>
/// FLAT, and a superset of <see cref="UserDto"/> (the advisor's review, blocking): every installed customer app reads
/// this answer's fields at the top level, so the consent list is added beside them, never around them. A test pins
/// the superset. <see cref="UserDto"/> itself is unchanged: it also rides the sign-in, refresh and profile answers.
/// </remarks>
/// <param name="PendingConsents">Always empty for an administrator, whom the texts do not address (owner, 2026-10-07).</param>
public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string FullName,
    string Phone,
    string Role,
    bool IsEmailVerified,
    bool MustChangePassword,
    DateTimeOffset CreatedAt,
    IReadOnlyList<LegalConfigDocumentDto> PendingConsents)
{
    public static CurrentUserDto From(UserDto user, IReadOnlyList<LegalConfigDocumentDto> pendingConsents)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(pendingConsents);
        return new CurrentUserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.Phone,
            user.Role,
            user.IsEmailVerified,
            user.MustChangePassword,
            user.CreatedAt,
            pendingConsents);
    }
}

public sealed record GetCurrentUserQuery(Id UserId) : IQuery<Result<CurrentUserDto, Error>>;

public sealed class GetCurrentUserHandler(
    IUserRepository users,
    ILegalConsentReader consents,
    ICustomerSiteSettings site,
    IClock clock)
    : IRequestHandler<GetCurrentUserQuery, Result<CurrentUserDto, Error>>
{
    public async Task<Result<CurrentUserDto, Error>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;

        IReadOnlyList<PendingLegalVersion> pending =
            user.Role == UserRole.Admin ? [] : await consents.PendingAsync(user.Id, clock.UtcNow, cancellationToken);
        return CurrentUserDto.From(UserDto.From(user), [.. pending.Select(row => LegalPageLinks.Describe(site, row))]);
    }
}
