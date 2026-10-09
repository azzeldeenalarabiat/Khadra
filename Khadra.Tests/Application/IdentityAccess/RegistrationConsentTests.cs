using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.RegisterCustomer;
using Khadra.Application.IdentityAccess.RegisterDealerOwner;
using Khadra.Application.Legal;
using Khadra.Domain.Legal;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.IdentityAccess;

/// <summary>
/// A registration accepts the legal texts in force in the same save as the account (Wave 4, W4-8): required of the
/// website and the console (W4-D7), spared for the customer app until a build asks (1.4.0).
/// </summary>
public sealed class RegistrationConsentTests
{
    private static readonly ClientInfo Website = new("1.2.3.4", "Mozilla/5.0");
    private static readonly ClientInfo OlderApp = new("1.2.3.4", "Dart/3.5", AppVersion.Parse("1.3.0"));
    private static readonly ClientInfo ConsentAwareApp = new("1.2.3.4", "Dart/3.5", AppVersion.Parse("1.4.0+7"));

    private static RegisterCustomerCommand Customer(ConsentInput? consent, ClientInfo client) =>
        new("Ali@Example.com", "Passw0rd1", "Ali Ahmad", "079 123 4567", Users.AdultBirthDate, false, consent, client);

    private static RegisterDealerOwnerCommand Owner(ConsentInput? consent) =>
        new("Owner@Gallery.jo", "Passw0rd1", "Rami Odeh", "079 555 4444", consent);

    [Fact]
    public async Task The_website_cannot_register_a_customer_without_the_texts_in_force_and_nothing_is_saved()
    {
        var context = new AuthHandlerTestContext();
        context.Legal.PublishBoth();

        var result = await new RegisterCustomerHandler(context.Registrar).Handle(Customer(null, Website), CancellationToken.None);

        Assert.Equal(LegalErrors.ConsentRequired.Code, result.Error.Code);
        Assert.Empty(context.Legal.Staged);
        Assert.Empty(context.SentEmails());
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_website_registration_records_each_text_in_its_own_save()
    {
        var context = new AuthHandlerTestContext();
        var (terms, privacy) = context.Legal.PublishBoth();

        var result = await new RegisterCustomerHandler(context.Registrar).Handle(
            Customer(new ConsentInput([terms.Value, privacy.Value], "ar"), Website), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.ConsentsRecorded);
        var user = Assert.Single(context.AddedUsers);
        Assert.All(context.Legal.Staged, consent =>
        {
            Assert.Equal(user.Id, consent.UserId);
            Assert.Same(ConsentChannel.Website, consent.Channel);
            Assert.Equal("ar", consent.Language.Name);
        });
        // The consents ride the account's one save.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An installed build older than 1.4.0 cannot be patched, only refused: a registration it never knew to send must not
    /// fail under it, until the minimum refuses the build outright (a temporary bridge; pre-launch item 239).
    /// </summary>
    [Fact]
    public async Task An_app_build_older_than_the_consent_release_registers_without_them()
    {
        var context = new AuthHandlerTestContext();
        context.Legal.PublishBoth();

        var result = await new RegisterCustomerHandler(context.Registrar).Handle(Customer(null, OlderApp), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.ConsentsRecorded);
        Assert.Empty(context.Legal.Staged);
    }

    /// <summary>From 1.4.0 the app asks, so the server holds it to the answer as it holds the website (item 238).</summary>
    [Fact]
    public async Task The_consent_asking_app_cannot_register_without_the_texts_in_force_and_nothing_is_saved()
    {
        var context = new AuthHandlerTestContext();
        context.Legal.PublishBoth();

        var result = await new RegisterCustomerHandler(context.Registrar).Handle(Customer(null, ConsentAwareApp), CancellationToken.None);

        Assert.Equal(LegalErrors.ConsentRequired.Code, result.Error.Code);
        Assert.Empty(context.Legal.Staged);
        Assert.Empty(context.SentEmails());
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Where consent was given is the channel, whether or not it was required of that build.</summary>
    [Fact]
    public async Task An_app_build_that_does_send_them_records_them_as_the_apps()
    {
        foreach (var app in new[] { ConsentAwareApp, OlderApp })
        {
            var context = new AuthHandlerTestContext();
            var (terms, privacy) = context.Legal.PublishBoth();

            var result = await new RegisterCustomerHandler(context.Registrar).Handle(
                Customer(new ConsentInput([terms.Value, privacy.Value], "en"), app), CancellationToken.None);

            Assert.Equal(2, result.Value.ConsentsRecorded);
            Assert.All(context.Legal.Staged, consent => Assert.Same(ConsentChannel.App, consent.Channel));
        }
    }

    [Fact]
    public async Task A_superseded_version_is_refused_so_the_screen_can_reload_and_ask_again()
    {
        var context = new AuthHandlerTestContext();
        context.Legal.PublishBoth();

        var result = await new RegisterCustomerHandler(context.Registrar).Handle(
            Customer(new ConsentInput([Guid.NewGuid(), Guid.NewGuid()], "en"), Website), CancellationToken.None);

        Assert.Equal(LegalErrors.VersionNotCurrent.Code, result.Error.Code);
        Assert.Empty(context.Legal.Staged);
    }

    [Fact]
    public async Task With_nothing_in_force_a_registration_needs_no_consent()
    {
        var context = new AuthHandlerTestContext();

        var result = await new RegisterCustomerHandler(context.Registrar).Handle(Customer(null, Website), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.ConsentsRecorded);
    }

    [Fact]
    public async Task The_console_cannot_register_an_owner_without_them_and_records_them_as_the_consoles()
    {
        var context = new AuthHandlerTestContext();
        var (terms, privacy) = context.Legal.PublishBoth();

        var refused = await new RegisterDealerOwnerHandler(context.Registrar).Handle(Owner(null), CancellationToken.None);
        var accepted = await new RegisterDealerOwnerHandler(context.Registrar).Handle(
            Owner(new ConsentInput([terms.Value, privacy.Value], "en")), CancellationToken.None);

        Assert.Equal(LegalErrors.ConsentRequired.Code, refused.Error.Code);
        Assert.Equal(2, accepted.Value.ConsentsRecorded);
        Assert.All(context.Legal.Staged, consent => Assert.Same(ConsentChannel.Console, consent.Channel));
    }

    [Fact]
    public void A_registration_that_accepts_texts_says_which_language_they_were_read_in()
    {
        var validator = new RegisterCustomerCommandValidator();

        var withoutLanguage = validator.Validate(Customer(new ConsentInput([Guid.NewGuid()], null), Website));
        var withLanguage = validator.Validate(Customer(new ConsentInput([Guid.NewGuid()], "ar"), Website));
        var withNothing = validator.Validate(Customer(null, Website));

        Assert.False(withoutLanguage.IsValid);
        Assert.True(withLanguage.IsValid);
        Assert.True(withNothing.IsValid);
    }
}
