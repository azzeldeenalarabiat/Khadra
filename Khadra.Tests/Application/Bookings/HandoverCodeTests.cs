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

    /// <summary>The booking as the handler will load it, and the clock at <paramref name="at"/>: by default the moment
    /// its next handover may be recorded, which is the earliest a code is issued (pre-launch item 225).</summary>
    private Booking Given(Booking booking, Id? caller = null, DateTimeOffset? at = null)
    {
        _bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _actor.UserId.Returns(caller ?? booking.CustomerId);
        _clock.UtcNow = at ?? (booking.Status == BookingStatus.PickedUp ? booking.ReturnAvailableFrom : booking.PickupAvailableFrom);
        return booking;
    }

    private static ClientInfo App(string version) => new("1.2.3.4", "Dart/3.5", AppVersion.Parse(version));

    private Task<CSharpFunctionalExtensions.Result<HandoverCodeDto, Error>> Issue(Booking booking, ClientInfo? client = null) =>
        new IssueHandoverCodeHandler(_bookings, _codes, Service, _settings, _actor, _unitOfWork, _clock)
            .Handle(new IssueHandoverCodeCommand(booking.Id, client), CancellationToken.None);

    [Fact]
    public async Task A_confirmed_booking_gets_a_six_digit_pickup_code_stored_only_as_a_hash()
    {
        var booking = Given(Build.ConfirmedBooking());

        var dto = (await Issue(booking)).Value;

        Assert.Equal("Pickup", dto.Type);
        Assert.Matches("^[0-9]{6}$", dto.Code);
        Assert.Equal($"khadra-handover:v1:{booking.Reference.Value}:{dto.Code}", dto.QrPayload);
        Assert.Equal(_clock.UtcNow.AddMinutes(15), dto.ExpiresAt);
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
        var now = _clock.UtcNow;
        var previous = HandoverCode.Issue(booking.Id, HandoverType.Pickup, new string('a', 64), now.AddMinutes(-5), TimeSpan.FromMinutes(15));
        _codes.ListCurrentAsync(booking.Id, HandoverType.Pickup, Arg.Any<CancellationToken>()).Returns([previous]);

        await Issue(booking);

        Assert.Equal(now, previous.SupersededAt);
        Assert.Equal("handover.code_invalid", previous.Verify(true, 5, Id.New(), now).Error.Code);
    }

    // ── When a code may be asked for (pre-launch item 225; Wave 7) ────────────────────────────────────────────────────

    /// <summary>
    /// The same predicate the office's recording reads, so issuing and recording cannot drift apart: a code cannot
    /// predate the moment the handover it proves may be recorded, and the refusal names that moment so the website and
    /// the app can say when. The website and the console declare no version, so they are held to it from the deploy.
    /// </summary>
    [Fact]
    public async Task Before_the_pickup_window_opens_no_code_is_issued_and_the_refusal_says_when()
    {
        var booking = Given(Build.ConfirmedBooking());

        foreach (var client in new[] { ClientInfo.Unknown, App("1.4.0"), App("1.4.0+7"), App("1.10.0") })
        {
            _clock.UtcNow = booking.PickupAvailableFrom.AddMinutes(-1);

            var refused = await Issue(booking, client);

            Assert.Equal("booking.pickup_too_early", refused.Error.Code);
            Assert.Equal(booking.PickupAvailableFrom, refused.Error.Extensions!["availableFrom"]);
        }

        Assert.Empty(_issued);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_code_is_issued_the_moment_the_window_opens()
    {
        var booking = Given(Build.ConfirmedBooking());

        Assert.Equal(booking.PickupAvailableFrom, _clock.UtcNow);
        Assert.Equal("Pickup", (await Issue(booking, App("1.4.0+7"))).Value.Type);
    }

    /// <summary>A car collected early, inside the turnaround, cannot be returned before the rental starts, so it gets no
    /// return code before then either.</summary>
    [Fact]
    public async Task No_return_code_is_issued_before_the_rental_starts()
    {
        var booking = Build.ConfirmedBooking();
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.PickupAvailableFrom).IsSuccess);
        Given(booking, at: booking.Period.Start.AddMinutes(-1));

        var refused = await Issue(booking, App("1.4.0+7"));

        Assert.Equal("booking.return_too_early", refused.Error.Code);
        Assert.Equal(booking.Period.Start, refused.Error.Extensions!["availableFrom"]);
        Assert.Empty(_issued);
    }

    /// <summary>
    /// An installed build older than 1.4.0 offers the code on any confirmed booking and cannot be patched, only refused:
    /// it is answered as before until the minimum refuses it outright (a temporary bridge, not a security boundary --
    /// recording is still refused before the window, and that is what an early code would have to get past; item 239).
    /// </summary>
    [Fact]
    public async Task An_app_build_older_than_the_release_is_still_given_an_early_code_until_the_minimum_rises()
    {
        var booking = Given(Build.ConfirmedBooking(), at: Build.Now);

        Assert.True(Build.Now < booking.PickupAvailableFrom);
        Assert.Equal("Pickup", (await Issue(booking, App("1.3.0"))).Value.Type);
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
