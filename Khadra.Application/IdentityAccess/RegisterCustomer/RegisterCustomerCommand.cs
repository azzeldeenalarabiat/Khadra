using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.IdentityAccess.RegisterCustomer;

public sealed record RegisterCustomerCommand(
    string Email,
    string Password,
    string FullName,
    string Phone) : ICommand<Result<RegisteredUserDto, Error>>;

public sealed class RegisterCustomerCommandValidator : AbstractValidator<RegisterCustomerCommand>
{
    public RegisterCustomerCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(PasswordPolicy.MaximumLength);
        RuleFor(command => command.FullName).NotEmpty().MaximumLength(PersonName.MaxLength);
        RuleFor(command => command.Phone).NotEmpty().MaximumLength(32);
    }
}
