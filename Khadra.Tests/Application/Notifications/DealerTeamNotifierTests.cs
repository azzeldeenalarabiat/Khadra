using Khadra.Application.Notifications;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Notifications;

// Spec 4.2: a dealership is a team, and every booking action names who took it. These are the rules
// that decide who hears about it -- the owner and every ACTIVE colleague, never the person who did it,
// and never anyone at another dealership.
public sealed class DealerTeamNotifierTests
{
    private static readonly Id OwnerId = Id.New();
    private static readonly Id EmployeeId = Id.New();
    private static readonly Id OtherEmployeeId = Id.New();

    private sealed class Context
    {
        public INotifier Notifier { get; } = Substitute.For<INotifier>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public Dealer Dealer { get; }

        public Context()
        {
            Dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        }

        public DealerTeamNotifier Notifier_() => new(Notifier, Users);

        public List<Notification> Raised { get; } = [];

        public DealerTeamNotifier Capturing()
        {
            Notifier
                .When(n => n.RaiseMany(Arg.Any<IEnumerable<Notification>>()))
                .Do(call => Raised.AddRange(call.Arg<IEnumerable<Notification>>()));
            Notifier
                .When(n => n.Raise(Arg.Any<Notification>()))
                .Do(call => Raised.Add(call.Arg<Notification>()));
            return Notifier_();
        }
    }

    [Fact]
    public async Task An_employees_action_reaches_the_owner_and_the_other_staff_but_not_themselves()
    {
        var context = new Context();
        context.Dealer.HireEmployee(EmployeeId, canViewReports: false, Build.Now);
        context.Dealer.HireEmployee(OtherEmployeeId, canViewReports: false, Build.Now);

        await context.Capturing().NotifyTeamAsync(
            context.Dealer,
            EmployeeId,
            NotificationKind.BookingApproved,
            Build.Now,
            Id.New(),
            "KR-1042");

        var recipients = context.Raised.Select(n => n.RecipientUserId).ToList();
        Assert.Contains(OwnerId, recipients);
        Assert.Contains(OtherEmployeeId, recipients);
        Assert.DoesNotContain(EmployeeId, recipients);
        Assert.All(context.Raised, n => Assert.Equal("KR-1042", n.SubjectReference));
    }

    [Fact]
    public async Task A_deactivated_employee_is_told_nothing()
    {
        var context = new Context();
        var employee = context.Dealer.HireEmployee(EmployeeId, canViewReports: false, Build.Now).Value;
        context.Dealer.HireEmployee(OtherEmployeeId, canViewReports: false, Build.Now);
        context.Dealer.DeactivateEmployee(employee.Id, Build.Now);

        await context.Capturing().NotifyTeamAsync(
            context.Dealer, OtherEmployeeId, NotificationKind.BookingPickedUp, Build.Now);

        var recipients = context.Raised.Select(n => n.RecipientUserId).ToList();
        Assert.Contains(OwnerId, recipients);
        Assert.DoesNotContain(EmployeeId, recipients);
    }

    [Fact]
    public async Task An_owner_acting_alone_notifies_nobody()
    {
        var context = new Context();

        await context.Capturing().NotifyTeamAsync(
            context.Dealer, OwnerId, NotificationKind.BookingApproved, Build.Now);

        Assert.Empty(context.Raised);
    }

    [Fact]
    public async Task Nobody_is_told_about_something_they_did_to_themselves()
    {
        var context = new Context();

        await context.Capturing().NotifyPersonAsync(
            OwnerId, OwnerId, NotificationKind.ReportAccessGranted, Build.Now);

        Assert.Empty(context.Raised);
    }

    [Fact]
    public async Task A_missing_actor_degrades_to_a_label_rather_than_failing_the_action()
    {
        var context = new Context();
        context.Dealer.HireEmployee(EmployeeId, canViewReports: false, Build.Now);
        context.Users.GetByIdAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns((User?)null);

        await context.Capturing().NotifyTeamAsync(
            context.Dealer, EmployeeId, NotificationKind.BookingReturned, Build.Now);

        Assert.NotEmpty(context.Raised);
        Assert.All(context.Raised, n => Assert.False(string.IsNullOrWhiteSpace(n.ActorName)));
    }
}
