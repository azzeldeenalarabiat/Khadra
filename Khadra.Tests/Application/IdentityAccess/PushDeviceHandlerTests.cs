using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.PushDevices;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.IdentityAccess;

/// <summary>Registering the caller's own phone for push, removing it, and recording their language.</summary>
public sealed class PushDeviceHandlerTests
{
    private readonly IPushDeviceRepository _devices = Substitute.For<IPushDeviceRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentActor _actor = Substitute.For<ICurrentActor>();
    private readonly TestClock _clock = new(Users.Now);
    private readonly Guid _session = Guid.NewGuid();
    private readonly User _user = Users.Customer();

    public PushDeviceHandlerTests()
    {
        _actor.UserId.Returns(_user.Id);
        _actor.SessionId.Returns(_session);
        _users.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
    }

    private RegisterMyPushDeviceHandler Register() => new(_devices, _unitOfWork, _actor, _clock);

    [Fact]
    public async Task A_new_token_is_registered_to_the_caller_and_this_session()
    {
        var result = await Register().Handle(
            new RegisterMyPushDeviceCommand("tok", "android", "AR", "1.2.0+3"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _devices.Received(1).AddAsync(
            Arg.Is<PushDevice>(d => d.Token == "tok"
                                    && d.UserId == _user.Id
                                    && d.SessionFamilyId == _session
                                    && d.Platform == PushPlatform.Android
                                    && d.Language == Language.Arabic),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_known_token_moves_to_whoever_registers_it_last()
    {
        var previousOwner = Id.New();
        var device = PushDevice.Register("tok", PushPlatform.Android, previousOwner, Guid.NewGuid(), Language.English, null, Users.Now);
        device.Revoke(Users.Now);
        _devices.GetByTokenAsync("tok", Arg.Any<CancellationToken>()).Returns(device);
        _clock.UtcNow = Users.Now.AddHours(1);

        await Register().Handle(new RegisterMyPushDeviceCommand("tok", "Android", "ar", null), CancellationToken.None);

        Assert.Equal(_user.Id, device.UserId);
        Assert.Equal(_session, device.SessionFamilyId);
        Assert.Same(Language.Arabic, device.Language);
        Assert.False(device.IsRevoked);
        Assert.Equal(Users.Now.AddHours(1), device.LastSeenAt);
        await _devices.DidNotReceive().AddAsync(Arg.Any<PushDevice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Two_registrations_of_the_same_new_token_racing_is_not_an_error()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new UniqueConstraintConflictException("duplicate token"));

        var result = await Register().Handle(new RegisterMyPushDeviceCommand("tok", "Android", "en", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("", "Android", "en")]
    [InlineData("tok", "Windows", "en")]
    [InlineData("tok", "Android", "fr")]
    public void The_validator_refuses_what_the_platform_cannot_address(string token, string platform, string language)
    {
        var result = new RegisterMyPushDeviceCommandValidator()
            .Validate(new RegisterMyPushDeviceCommand(token, platform, language, null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void The_token_never_appears_in_the_commands_text()
    {
        var text = new RegisterMyPushDeviceCommand("secret-fcm-token", "Android", "en", "1.2.0").ToString();

        Assert.DoesNotContain("secret-fcm-token", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removing_revokes_only_the_devices_of_this_session()
    {
        var result = await new RemoveMyPushDeviceHandler(_devices, _actor, _clock)
            .Handle(new RemoveMyPushDeviceCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _devices.Received(1).RevokeForSessionAsync(_user.Id, _session, Users.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_on_a_token_with_no_session_is_a_quiet_success()
    {
        _actor.SessionId.Returns((Guid?)null);

        var result = await new RemoveMyPushDeviceHandler(_devices, _actor, _clock)
            .Handle(new RemoveMyPushDeviceCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _devices.DidNotReceiveWithAnyArgs().RevokeForSessionAsync(default, default, default, default);
    }

    [Fact]
    public async Task Setting_the_language_records_it_on_the_account()
    {
        var result = await new SetMyLanguageHandler(_users, _unitOfWork, _actor)
            .Handle(new SetMyLanguageCommand("AR"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(Language.Arabic, _user.PreferredLanguage);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_language_the_platform_does_not_speak_is_refused()
    {
        Assert.False(new SetMyLanguageCommandValidator().Validate(new SetMyLanguageCommand("fr")).IsValid);
    }
}
