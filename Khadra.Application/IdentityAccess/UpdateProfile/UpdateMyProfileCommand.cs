using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.UpdateProfile;

/// <summary>
/// A person correcting their own name and phone number.
/// </summary>
/// <remarks>
/// Deliberately narrow. Email is the sign-in identifier and the password-reset destination, so it
/// needs a verified change flow of its own (pre-launch checklist item 44) and is not here. Date of
/// birth and foreign-national status were the inputs to the age check at registration; a customer
/// editing them would be editing the answer to a check the platform already performed against the
/// documents on file.
/// </remarks>
public sealed record UpdateMyProfileCommand(Id UserId, string FullName, string Phone)
    : ICommand<Result<UserDto, Error>>;

public sealed class UpdateMyProfileCommandValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileCommandValidator()
    {
        RuleFor(command => command.FullName).NotEmpty().MaximumLength(150);
        RuleFor(command => command.Phone).NotEmpty().MaximumLength(32);
    }
}

public sealed class UpdateMyProfileHandler(IUserRepository users, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateMyProfileCommand, Result<UserDto, Error>>
{
    public async Task<Result<UserDto, Error>> Handle(
        UpdateMyProfileCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;

        var name = PersonName.Create(request.FullName);
        if (name.IsFailure)
            return name.Error;

        var phone = PhoneNumber.Create(request.Phone);
        if (phone.IsFailure)
            return phone.Error;

        // PhoneNumber.Create normalises "07…" to "+9627…", so this compares the stored form against
        // the stored form. Comparing the raw input would let the same number through as "changed"
        // and then collide with itself on the uniqueness check below.
        if (user.Phone != phone.Value &&
            await users.ExistsByPhoneAsync(phone.Value, cancellationToken))
        {
            return IdentityErrors.PhoneTaken;
        }

        user.UpdateContactDetails(name.Value, phone.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserDto.From(user);
    }
}
