using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Application.Legal;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Legal;
using MediatR;

namespace Khadra.Application.IdentityAccess.RegisterDealerOwner;

/// <summary>
/// Step one of spec 3.1: the person behind a rental office gets an account.
///
/// The account is created as DealerOwner straight away, matching spec 1 and the policies already in
/// the API. It is the DEALER that starts PENDING_REVIEW, not the user: the role says what someone is,
/// the dealer's verification status decides what they may do. Keeping those separate means approval
/// never has to mutate an identity or invalidate a live session.
///
/// No date of birth is asked for. Spec 5.1's minimum age governs who may RENT a car; the person who
/// owns the rental office is not renting one, and the platform's proof of who they are is the
/// identity document an administrator reads at licence review (spec 3.1), not a date they typed.
/// </summary>
public sealed record RegisterDealerOwnerCommand(
    string Email,
    string Password,
    string FullName,
    string Phone,
    // The legal texts the console showed and the owner accepted (Wave 4, W4-8): required while a text is in force.
    ConsentInput? Consent = null) : ICommand<Result<RegisteredUserDto, Error>>;

public sealed class RegisterDealerOwnerCommandValidator : AbstractValidator<RegisterDealerOwnerCommand>
{
    public RegisterDealerOwnerCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(PasswordPolicy.MaximumLength);
        RuleFor(command => command.FullName).NotEmpty().MaximumLength(PersonName.MaxLength);
        RuleFor(command => command.Phone).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Consent!).SetValidator(new ConsentInputValidator()).When(command => command.Consent is not null);
    }
}

public sealed class RegisterDealerOwnerHandler(AccountRegistrar registrar)
    : IRequestHandler<RegisterDealerOwnerCommand, Result<RegisteredUserDto, Error>>
{
    public Task<Result<RegisteredUserDto, Error>> Handle(
        RegisterDealerOwnerCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return registrar.RegisterAsync(
            request.Email,
            request.Phone,
            request.FullName,
            request.Password,
            dateOfBirth: null,
            enforceMinimumAge: false,
            (email, phone, name, hash, now, _) =>
                User.RegisterDealerOwner(email, phone, name, hash, now),
            // Only the console registers an office's owner, and the console must ask (W4-D7).
            new ConsentRequest(request.Consent ?? ConsentInput.None, ConsentChannel.Console, Required: true),
            cancellationToken);
    }
}
