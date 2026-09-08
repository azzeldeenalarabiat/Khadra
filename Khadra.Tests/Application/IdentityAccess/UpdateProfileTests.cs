using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.UpdateProfile;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.IdentityAccess;

/// <summary>
/// A customer correcting their own name and phone number (pre-launch checklist item 44, in part).
///
/// The narrowness is the design. Email is the sign-in identifier and the password-reset destination,
/// so it needs a verified change flow and is deliberately not reachable here.
/// </summary>
public sealed class UpdateProfileTests
{
    private sealed class Context
    {
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public User Customer { get; }

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Customer = Build.Customer();
            Users.GetByIdAsync(Customer.Id, Arg.Any<CancellationToken>()).Returns(Customer);
        }

        public UpdateMyProfileHandler Handler() => new(Users, UnitOfWork);
    }

    [Fact]
    public async Task A_customer_corrects_their_own_name_and_phone()
    {
        var context = new Context();

        var result = await context.Handler().Handle(
            new UpdateMyProfileCommand(context.Customer.Id, "Layla Odeh Al-Masri", "0791234567"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal("Layla Odeh Al-Masri", context.Customer.Name.Value);
        // Normalised on the way in, so the value the app displays is the value the platform stores.
        Assert.Equal("+962791234567", context.Customer.Phone.Value);
        Assert.Equal(context.Customer.Phone.Value, result.Value.Phone);
    }

    /// <summary>
    /// Renaming yourself is not a security event. Rotating the stamp would sign the person out of
    /// every device they own for correcting a typo.
    /// </summary>
    [Fact]
    public async Task Correcting_a_name_does_not_end_the_persons_sessions()
    {
        var context = new Context();
        var stamp = context.Customer.SecurityStamp;

        await context.Handler().Handle(
            new UpdateMyProfileCommand(context.Customer.Id, "Layla O.", context.Customer.Phone.Value),
            CancellationToken.None);

        Assert.Equal(stamp, context.Customer.SecurityStamp);
    }

    [Fact]
    public async Task A_phone_number_somebody_else_holds_is_refused()
    {
        var context = new Context();
        context.Users.ExistsByPhoneAsync(Arg.Any<PhoneNumber>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await context.Handler().Handle(
            new UpdateMyProfileCommand(context.Customer.Id, "Layla Odeh", "0799999999"),
            CancellationToken.None);

        Assert.Equal("auth.phone_taken", result.Error.Code);
    }

    /// <summary>
    /// Saving your own number unchanged must not collide with yourself. The comparison is on the
    /// NORMALISED form, because "0791234567" and "+962791234567" are the same number and a raw
    /// comparison would call it a change and then find it taken.
    /// </summary>
    [Fact]
    public async Task Keeping_your_own_number_is_not_a_collision()
    {
        var context = new Context();
        context.Users.ExistsByPhoneAsync(Arg.Any<PhoneNumber>(), Arg.Any<CancellationToken>()).Returns(true);
        var localForm = context.Customer.Phone.Value.Replace("+962", "0", StringComparison.Ordinal);

        var result = await context.Handler().Handle(
            new UpdateMyProfileCommand(context.Customer.Id, "Layla Odeh", localForm),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }

    [Fact]
    public async Task An_invalid_phone_number_is_refused_by_the_value_object()
    {
        var context = new Context();

        var result = await context.Handler().Handle(
            new UpdateMyProfileCommand(context.Customer.Id, "Layla Odeh", "not-a-number"),
            CancellationToken.None);

        Assert.Equal("auth.invalid_phone", result.Error.Code);
    }

    [Fact]
    public void An_empty_name_is_refused_before_the_handler()
    {
        var validator = new UpdateMyProfileCommandValidator();

        Assert.False(validator.Validate(new UpdateMyProfileCommand(Id.New(), "  ", "0791234567")).IsValid);
        Assert.True(validator.Validate(new UpdateMyProfileCommand(Id.New(), "Layla", "0791234567")).IsValid);
    }
}
