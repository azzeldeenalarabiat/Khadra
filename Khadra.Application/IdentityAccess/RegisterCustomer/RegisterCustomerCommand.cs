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
    string Phone,
    // Spec 5.1: needed for the minimum-age check. Optional in the contract rather than the shape,
    // because whether it is required depends on a configured business rule, and RenterAgePolicy is
    // the single place that decides.
    DateOnly? DateOfBirth,
    bool IsForeignNational) : ICommand<Result<RegisteredUserDto, Error>>;

public sealed class RegisterCustomerCommandValidator : AbstractValidator<RegisterCustomerCommand>
{
    public RegisterCustomerCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(PasswordPolicy.MaximumLength);
        RuleFor(command => command.FullName).NotEmpty().MaximumLength(PersonName.MaxLength);
        RuleFor(command => command.Phone).NotEmpty().MaximumLength(32);
        // A bound this side of absurd; the real rule lives in RenterAgePolicy against the configured
        // minimum. This only catches typos like a year of 0195.
        RuleFor(command => command.DateOfBirth)
            .Must(date => date is null || date.Value.Year >= 1900)
            .WithMessage("The date of birth is not valid.");
    }
}
