using CSharpFunctionalExtensions;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using MediatR;

namespace Khadra.Application.IdentityAccess.RegisterCustomer;

public sealed class RegisterCustomerHandler(AccountRegistrar registrar)
    : IRequestHandler<RegisterCustomerCommand, Result<RegisteredUserDto, Error>>
{
    public Task<Result<RegisteredUserDto, Error>> Handle(
        RegisterCustomerCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return registrar.RegisterAsync(
            request.Email,
            request.Phone,
            request.FullName,
            request.Password,
            request.DateOfBirth,
            (email, phone, name, hash, now, dateOfBirth) =>
                User.RegisterCustomer(email, phone, name, hash, now, dateOfBirth, request.IsForeignNational),
            cancellationToken);
    }
}
