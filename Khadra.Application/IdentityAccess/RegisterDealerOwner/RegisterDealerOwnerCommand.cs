using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using MediatR;

namespace Khadra.Application.IdentityAccess.RegisterDealerOwner;

/// <summary>
/// Step one of spec 3.1: the person behind a rental office gets an account.
///
/// The account is created as DealerOwner straight away, matching spec 1 and the policies already in
/// the API. It is the DEALER that starts PENDING_REVIEW, not the user: the role says what someone is,
/// the dealer's verification status decides what they may do. Keeping those separate means approval
/// never has to mutate an identity or invalidate a live session.
/// </summary>
public sealed record RegisterDealerOwnerCommand(
    string Email,
    string Password,
    string FullName,
    string Phone,
    DateOnly? DateOfBirth) : ICommand<Result<RegisteredUserDto, Error>>;

public sealed class RegisterDealerOwnerCommandValidator : AbstractValidator<RegisterDealerOwnerCommand>
{
    public RegisterDealerOwnerCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(PasswordPolicy.MaximumLength);
        RuleFor(command => command.FullName).NotEmpty().MaximumLength(PersonName.MaxLength);
        RuleFor(command => command.Phone).NotEmpty().MaximumLength(32);
        RuleFor(command => command.DateOfBirth)
            .Must(date => date is null || date.Value.Year >= 1900)
            .WithMessage("The date of birth is not valid.");
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
            request.DateOfBirth,
            (email, phone, name, hash, now, dateOfBirth) =>
                User.RegisterDealerOwner(email, phone, name, hash, now, dateOfBirth),
            cancellationToken);
    }
}
