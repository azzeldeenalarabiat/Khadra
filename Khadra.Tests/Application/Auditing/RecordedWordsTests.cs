using System.Text.Json;
using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Notifications;
using Khadra.Application.PlatformSettings.Lookups;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Domain.PlatformSettings;
using Khadra.Domain.PlatformSettings.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Auditing;

/// <summary>
/// What is written, at the moment of an action, into records that are never rewritten (Fix &amp; Polish Wave 6, pre-launch
/// items 103, 174 and 176): codes and parts the screens word in their reader's language, never an English sentence
/// composed on the server.
/// </summary>
public sealed class RecordedWordsTests
{
    private readonly IAuditTrail _auditTrail = Substitute.For<IAuditTrail>();
    private readonly ICityRepository _cities = Substitute.For<ICityRepository>();
    private readonly ICurrentActor _actor = Substitute.For<ICurrentActor>();
    private readonly List<AuditEntry> _audited = [];

    public RecordedWordsTests()
    {
        _actor.UserId.Returns(Id.New());
        _actor.Role.Returns(UserRole.Admin);
        _actor.Name.Returns("Rania Haddad");
        _auditTrail.When(trail => trail.Record(Arg.Any<AuditEntry>())).Do(call => _audited.Add(call.Arg<AuditEntry>()));
        _cities.ListAsync(activeOnly: true, Arg.Any<CancellationToken>()).Returns([]);
    }

    private LookupHandlers Lookups() =>
        new(
            Substitute.For<ICarTypeRepository>(), _cities, new AdminActionRecorder(_auditTrail, _actor, new TestClock(Build.Now)),
            Substitute.For<IUnitOfWork>(), new TestClock(Build.Now));

    private static Dictionary<string, JsonElement> Parts(string? value)
    {
        Assert.NotNull(value);
        using var document = JsonDocument.Parse(value);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    [Fact]
    public async Task A_city_is_recorded_under_both_its_names_and_its_change_as_parts()
    {
        var result = await Lookups().Handle(new CreateCityCommand("Madaba", "مادبا", 1, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(_audited);
        Assert.Equal("Madaba", entry.SubjectLabel);
        Assert.Equal("مادبا", entry.SubjectLabelAr);
        Assert.Null(entry.PreviousValue);
        var parts = Parts(entry.NewValue);
        Assert.Equal("Madaba", parts["en"].GetString());
        Assert.Equal("مادبا", parts["ar"].GetString());
        Assert.True(parts["offered"].GetBoolean());
        // Arabic as itself, not \u escapes: the column is capped and somebody may read it in the table.
        Assert.Contains("مادبا", entry.NewValue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retiring_a_city_records_what_it_was_and_what_it_became()
    {
        var city = City.Create("Madaba", "مادبا", 1, Build.Now).Value;
        _cities.GetByIdAsync(city.Id, Arg.Any<CancellationToken>()).Returns(city);

        var result = await Lookups().Handle(new SetLookupActiveCommand(city.Id, LookupHandlers.CitiesKind, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(_audited);
        Assert.Same(AuditAction.LookupRetired, entry.Action);
        Assert.True(Parts(entry.PreviousValue)["offered"].GetBoolean());
        Assert.False(Parts(entry.NewValue)["offered"].GetBoolean());
        Assert.Equal("مادبا", entry.SubjectLabelAr);
    }

    /// <summary>
    /// Every token the platform issues carries a name, so this path is a wiring mistake; if it runs, it records the
    /// actor's short reference — the same in both languages — and never the English "Unknown admin".
    /// </summary>
    [Fact]
    public void An_actor_without_a_name_is_recorded_by_reference_not_in_English()
    {
        var id = Id.New();
        _actor.UserId.Returns(id);
        _actor.Name.Returns((string?)null);

        new AdminActionRecorder(_auditTrail, _actor, new TestClock(Build.Now))
            .Record(AuditAction.DealerApproved, AuditEntityType.Dealer, Id.New(), "Aqaba Coast Cars", "PendingReview", "Approved");

        var entry = Assert.Single(_audited);
        Assert.Equal(id.Value.ToString("N")[..8], entry.ActorName);
        Assert.Equal(id, entry.ActorUserId);
    }

    [Fact]
    public async Task A_colleague_whose_account_cannot_be_read_is_a_code_beside_the_old_phrase()
    {
        var raised = new List<Notification>();
        var notifier = Substitute.For<INotifier>();
        notifier.When(n => n.Raise(Arg.Any<Notification>())).Do(call => raised.Add(call.Arg<Notification>()));
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var actor = Id.New();

        await new DealerTeamNotifier(notifier, users)
            .NotifyPersonAsync(Id.New(), actor, NotificationKind.ReportAccessGranted, Build.Now);

        var notification = Assert.Single(raised);
        Assert.Same(NotificationStandIn.Colleague, notification.ActorStandIn);
        Assert.Equal("A colleague", notification.ActorName);
        Assert.Equal(actor, notification.ActorUserId);
    }

    [Fact]
    public async Task A_rental_office_that_left_is_a_code_and_a_named_one_is_its_name()
    {
        var raised = new List<Notification>();
        var notifier = Substitute.For<INotifier>();
        notifier.When(n => n.Raise(Arg.Any<Notification>())).Do(call => raised.Add(call.Arg<Notification>()));
        var team = new DealerTeamNotifier(notifier, Substitute.For<IUserRepository>());

        await team.NotifyCustomerAsync(Id.New(), "", NotificationKind.YourBookingExpired, Build.Now);
        await team.NotifyCustomerAsync(Id.New(), "Petra Wheels", NotificationKind.YourBookingApproved, Build.Now);

        Assert.Same(NotificationStandIn.RentalOffice, raised[0].ActorStandIn);
        Assert.Equal("The rental office", raised[0].ActorName);
        Assert.Null(raised[1].ActorStandIn);
        Assert.Equal("Petra Wheels", raised[1].ActorName);
        Assert.Null(NotificationItem.From(raised[1], Id.New()).ActorStandIn);
    }
}
