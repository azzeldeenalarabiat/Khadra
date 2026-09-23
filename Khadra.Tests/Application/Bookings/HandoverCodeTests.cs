using Khadra.Application.Bookings.Handover;
using Khadra.Application.Common;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Security;
using Khadra.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Khadra.Tests.Application.Bookings;

/// <summary>The customer's side of a handover: asking for a code, and what the code can and cannot do.</summary>
public sealed class HandoverCodeTests
{
    private static readonly HandoverCodeService Service = new(Options.Create(new JwtOptions { SigningKey = new string('k', 48) }));

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly IHandoverCodeRepository _codes = Substitute.For<IHandoverCodeRepository>();
    private readonly IHandoverSettings _settings = Substitute.For<IHandoverSettings>();
    private readonly ICurrentActor _actor = Substitute.For<ICurrentActor>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestClock _clock = new(Build.Now);
    private readonly List<HandoverCode> _issued = [];

    public HandoverCodeTests()
    {
        _settings.CodeLifetime.Returns(TimeSpan.FromMinutes(15));
        _codes.ListCurrentAsync(Arg.Any<Id>(), Arg.Any<HandoverType>(), Arg.Any<CancellationToken>()).Returns([]);
        _codes.When(c => c.AddAsync(Arg.Any<HandoverCode>(), Arg.Any<CancellationToken>()))
            .Do(call => _issued.Add(call.Arg<HandoverCode>()));
    }

    private Booking Given(Booking booking, Id? caller = null)
    {
        _bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _actor.UserId.Returns(caller ?? booking.CustomerId);
        return booking;
    }

    private Task<CSharpFunctionalExtensions.Result<HandoverCodeDto, Error>> Issue(Booking booking) =>
        new IssueHandoverCodeHandler(_bookings, _codes, Service, _settings, _actor, _unitOfWork, _clock)
            .Handle(new IssueHandoverCodeCommand(booking.Id), CancellationToken.None);

    [Fact]
    public async Task A_confirmed_booking_gets_a_six_digit_pickup_code_stored_only_as_a_hash()
    {
        var booking = Given(Build.ConfirmedBooking());

        var dto = (await Issue(booking)).Value;

        Assert.Equal("Pickup", dto.Type);
        Assert.Matches("^[0-9]{6}$", dto.Code);
        Assert.Equal($"khadra-handover:v1:{booking.Reference.Value}:{dto.Code}", dto.QrPayload);
        Assert.Equal(Build.Now.AddMinutes(15), dto.ExpiresAt);
        var stored = Assert.Single(_issued);
        Assert.DoesNotContain(dto.Code, stored.CodeHash, StringComparison.Ordinal);
        Assert.True(Service.Matches(stored.CodeHash, booking.Id, HandoverType.Pickup, dto.Code));
        Assert.DoesNotContain(dto.Code, dto.ToString(), StringComparison.Ordinal);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_car_that_is_out_gets_a_return_code()
    {
        var booking = Build.ConfirmedBooking();
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);
        Given(booking);

        Assert.Equal("Return", (await Issue(booking)).Value.Type);
    }

    [Fact]
    public async Task Asking_again_kills_the_previous_code()
    {
        var booking = Given(Build.ConfirmedBooking());
        var previous = HandoverCode.Issue(booking.Id, HandoverType.Pickup, new string('a', 64), Build.Now.AddMinutes(-5), TimeSpan.FromMinutes(15));
        _codes.ListCurrentAsync(booking.Id, HandoverType.Pickup, Arg.Any<CancellationToken>()).Returns([previous]);

        await Issue(booking);

        Assert.Equal(Build.Now, previous.SupersededAt);
        Assert.Equal("handover.code_invalid", previous.Verify(true, 5, Id.New(), Build.Now).Error.Code);
    }

    [Fact]
    public async Task Somebody_elses_booking_looks_like_no_booking_at_all()
    {
        var booking = Given(Build.ConfirmedBooking(), caller: Id.New());

        Assert.Equal("booking.not_found", (await Issue(booking)).Error.Code);
        Assert.Empty(_issued);
    }

    [Fact]
    public async Task There_is_nothing_to_prove_before_the_deposit_or_after_the_return()
    {
        var unpaid = Given(Build.ApprovedBooking());
        Assert.Equal("handover.not_available", (await Issue(unpaid)).Error.Code);

        var requested = Given(Build.Booking());
        Assert.Equal("handover.not_available", (await Issue(requested)).Error.Code);
    }

    [Fact]
    public void A_code_is_spent_by_the_handover_it_proves()
    {
        var code = HandoverCode.Issue(Id.New(), HandoverType.Pickup, new string('a', 64), Build.Now, TimeSpan.FromMinutes(15));

        Assert.True(code.Verify(true, 5, Id.New(), Build.Now.AddMinutes(1)).IsSuccess);
        Assert.Equal("handover.code_used", code.Verify(true, 5, Id.New(), Build.Now.AddMinutes(2)).Error.Code);
    }

    [Fact]
    public void A_code_is_stored_as_a_hash_of_the_right_length_or_not_at_all()
    {
        Assert.Throws<DomainException>(() => HandoverCode.Issue(Id.New(), HandoverType.Pickup, "123456", Build.Now, TimeSpan.FromMinutes(15)));
        Assert.Throws<DomainException>(() => HandoverCode.Issue(Id.Empty, HandoverType.Pickup, new string('a', 64), Build.Now, TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void Codes_are_spread_across_the_whole_range()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => Service.Generate()).ToList();

        Assert.All(codes, c => Assert.Matches("^[0-9]{6}$", c));
        Assert.True(codes.Distinct().Count() > 190);
    }
}
