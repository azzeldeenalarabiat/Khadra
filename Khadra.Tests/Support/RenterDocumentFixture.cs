using Khadra.Application.Auditing;
using Khadra.Application.Bookings.RenterDocuments;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using NSubstitute;

namespace Khadra.Tests.Support;

/// <summary>
/// One gallery, one renter with a full set of papers, and the collaborators the renter-document
/// handlers need.
/// </summary>
/// <remarks>
/// Shared by the access tests and the review tests rather than copied into each, because the fixture
/// encodes the thing both are about — who the actor is, and which dealership they speak for — and two
/// copies of that would drift in the direction that makes a test pass for the wrong reason.
/// </remarks>
internal sealed class RenterDocumentFixture
{
    public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
    public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
    public FakeDocumentStorage Storage { get; } = new();
    public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
    public IDocumentAccessLog AccessLog { get; } = Substitute.For<IDocumentAccessLog>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
    public ICurrentActor Actor { get; } = Substitute.For<ICurrentActor>();
    public TestClock Clock { get; }

    /// <summary>Every disclosure record the handlers staged, in the order they staged it.</summary>
    public List<DocumentAccessEntry> Recorded { get; } = [];

    public Dealer Dealer { get; }
    public Id OwnerUserId { get; }
    public User Renter { get; }

    public RenterDocumentFixture(bool trading = true, DateTimeOffset? now = null)
    {
        var moment = now ?? Build.Now;
        Clock = new TestClock(moment);
        OwnerUserId = Id.New();
        Dealer = trading ? Build.ApprovedDealer(moment, OwnerUserId) : Suspended(OwnerUserId, moment);
        Dealers.GetByOwnerUserIdAsync(OwnerUserId, Arg.Any<CancellationToken>()).Returns(Dealer);

        Renter = Build.Customer(moment);
        Users.GetByIdAsync(Renter.Id, Arg.Any<CancellationToken>()).Returns(Renter);

        // The actor is whatever the VALIDATED TOKEN says. Nothing in a request body can reach these
        // fields, which is why a reviewer's identity cannot be forged from a browser.
        Actor.UserId.Returns(OwnerUserId);
        Actor.Role.Returns(UserRole.DealerOwner);
        Actor.Name.Returns("Rami Haddad");
        Actor.CorrelationId.Returns("test-correlation");

        AccessLog
            .When(log => log.Record(Arg.Any<DocumentAccessEntry>()))
            .Do(call => Recorded.Add(call.Arg<DocumentAccessEntry>()));
    }

    /// <summary>Speaks for somebody else — an employee, say — the way a different token would.</summary>
    public void ActingAs(Id userId, string name = "Layla Haddad", UserRole? role = null)
    {
        Actor.UserId.Returns(userId);
        Actor.Name.Returns(name);
        Actor.Role.Returns(role ?? UserRole.DealerEmployee);
    }

    private static Dealer Suspended(Id ownerUserId, DateTimeOffset now)
    {
        var dealer = Build.ApprovedDealer(now, ownerUserId);
        dealer.Suspend(Id.New(), "Under investigation.", now);
        return dealer;
    }

    /// <summary>Hires an employee and makes the repository answer for them the way EF would.</summary>
    public Id HireEmployee(bool active = true)
    {
        var userId = Id.New();
        var employee = Dealer.HireEmployee(userId, canViewReports: false, Clock.UtcNow).Value;
        if (!active)
            Dealer.DeactivateEmployee(employee.Id, Clock.UtcNow);

        // The staff lookup matches an inactive row too, on purpose: the resolver is what decides they
        // have no standing, and the tests exist to prove it still does.
        Dealers.GetByStaffUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(Dealer);
        return userId;
    }

    public Booking Given(Booking booking)
    {
        Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        return booking;
    }

    public Booking ConfirmedBooking() =>
        Given(Build.ConfirmedBooking(Clock.UtcNow, customerId: Renter.Id, dealerId: Dealer.Id));

    public Booking RequestedBooking() =>
        Given(Build.Booking(Clock.UtcNow, customerId: Renter.Id, dealerId: Dealer.Id));

    public CustomerDocument LicenceFront() =>
        Renter.Documents.First(document => document.Type == CustomerDocumentType.DrivingLicenceFront);

    public RenterDocumentHandlers Handlers() => Handlers(Storage);

    public RenterDocumentHandlers Handlers(IDocumentStorage storage) => new(
        Bookings,
        Users,
        storage,
        new DealerMembershipResolver(Dealers),
        new DocumentAccessRecorder(AccessLog, Actor, Clock),
        Actor,
        Clock,
        UnitOfWork);
}
