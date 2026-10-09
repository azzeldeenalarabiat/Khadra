using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Application.Legal;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Legal;
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

        // The website must ask for the texts in force, and the server holds it to that (W4-D7); so must the customer app,
        // from the build that asks for them itself (1.4.0, pre-launch item 238). An app build older than that is spared
        // until the minimum refuses it outright: an installed build cannot be patched, only refused, and a registration
        // it never knew to send must not start failing under it (a temporary bridge, not a boundary; item 239). The
        // channel is where consent was given, whether or not it was required of that build.
        var client = request.Client ?? ClientInfo.Unknown;
        var consent = new ConsentRequest(
            request.Consent ?? ConsentInput.None,
            client.IsCustomerApp ? ConsentChannel.App : ConsentChannel.Website,
            Required: !client.PredatesRule(MobileAppContract.ConsentAwareFrom));

        return registrar.RegisterAsync(
            request.Email,
            request.Phone,
            request.FullName,
            request.Password,
            request.DateOfBirth,
            // Spec 5.1's minimum age is a rule about renters, and this is the flow that creates one.
            enforceMinimumAge: true,
            (email, phone, name, hash, now, dateOfBirth) =>
                User.RegisterCustomer(email, phone, name, hash, now, dateOfBirth, request.IsForeignNational),
            consent,
            cancellationToken);
    }
}
