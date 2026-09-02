using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.GetCurrentUser;

public sealed record GetCurrentUserQuery(Id UserId) : IQuery<Result<UserDto, Error>>;

public sealed class GetCurrentUserHandler(IUserRepository users)
    : IRequestHandler<GetCurrentUserQuery, Result<UserDto, Error>>
{
    public async Task<Result<UserDto, Error>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        return user is null ? IdentityErrors.UserNotFound : UserDto.From(user);
    }
}
