using Khadra.Application.AdminDashboard;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Domain.Common;

namespace Khadra.Tests.Application.AdminDashboard;

// The queue is the only screen on the dashboard that makes a judgement rather than reporting a count,
// so the judgement is pinned down here: what counts as overdue, when something starts warning, what
// gets grouped, and what an admin sees first.
public sealed class AttentionQueueBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    private const int SlaHours = 48;
    private const decimal WarningThreshold = 0.75m;

    private static LiveDispute Dispute(DateTimeOffset openedAt, DateTimeOffset deadline, string reason = "Damage disputed") =>
        new(Id.New(), Id.New(), reason, openedAt, deadline, IsAssigned: false);

    private static PendingDealerApplication Application(string name, DateTimeOffset submittedAt, DateTimeOffset dueAt) =>
        new(Id.New(), name, submittedAt, dueAt);

    private static AttentionQueueDtoWrapper Build(
        IReadOnlyCollection<LiveDispute>? disputes = null,
        IReadOnlyCollection<PendingDealerApplication>? applications = null,
        IReadOnlyDictionary<Id, string>? subtitles = null) =>
        new(AttentionQueueBuilder.Build(
            disputes ?? [],
            subtitles ?? new Dictionary<Id, string>(),
            applications ?? [],
            WarningThreshold,
            SlaHours,
            Now));

    // Small wrapper so the assertions below read as questions about the queue rather than indexing.
    private sealed record AttentionQueueDtoWrapper(Khadra.Application.AdminDashboard.Dtos.AttentionQueueDto Queue)
    {
        public IReadOnlyList<Khadra.Application.AdminDashboard.Dtos.AttentionItemDto> Items => Queue.Items;
    }

    [Fact]
    public void An_empty_platform_produces_an_empty_queue_rather_than_a_placeholder_row()
    {
        var result = Build();

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Queue.OpenCount);
        Assert.Equal(0, result.Queue.OverdueCount);
        Assert.Equal(SlaHours, result.Queue.SlaHours);
    }

    [Fact]
    public void A_dispute_past_its_frozen_deadline_is_overdue()
    {
        var overdue = Dispute(Now.AddHours(-61), Now.AddHours(-13));

        var result = Build(disputes: [overdue]);

        var item = Assert.Single(result.Items);
        Assert.Equal(AttentionQueueBuilder.Kinds.DisputeOverdue, item.Kind);
        Assert.Equal(AttentionQueueBuilder.Severities.Overdue, item.Severity);
        Assert.True(item.IsOverdue);
        Assert.Equal(1, result.Queue.OverdueCount);
    }

    [Fact]
    public void A_dispute_crosses_into_warning_at_the_threshold_and_not_before()
    {
        // A 48-hour window with a 75% threshold: 36 hours elapsed is the first warning moment.
        var justUnder = Dispute(Now.AddHours(-35), Now.AddHours(13));
        var justOver = Dispute(Now.AddHours(-36), Now.AddHours(12));

        Assert.Equal(
            AttentionQueueBuilder.Severities.Info,
            Assert.Single(Build(disputes: [justUnder]).Items).Severity);
        Assert.Equal(
            AttentionQueueBuilder.Severities.Warning,
            Assert.Single(Build(disputes: [justOver]).Items).Severity);
    }

    [Fact]
    public void Overdue_work_sorts_above_urgent_work_and_the_rest_sorts_by_deadline()
    {
        var soon = Dispute(Now.AddHours(-2), Now.AddHours(1), "expires soonest");
        var later = Dispute(Now.AddHours(-1), Now.AddHours(20), "expires later");
        var overdue = Dispute(Now.AddHours(-60), Now.AddHours(-12), "already breached");

        var items = Build(disputes: [later, soon, overdue]).Items;

        // Overdue first even though its deadline is the oldest, then soonest-first among the rest.
        Assert.Equal("already breached", items[0].Description);
        Assert.Equal("expires soonest", items[1].Description);
        Assert.Equal("expires later", items[2].Description);
    }

    [Fact]
    public void Dealer_applications_collapse_into_one_row_that_counts_them()
    {
        // The design shows "3 dealer applications are approaching the 48-hour SLA" as a single line.
        var applications = new[]
        {
            Application("Wadi Rum Motors", Now.AddHours(-47), Now.AddHours(1)),
            Application("Irbid Auto Lease", Now.AddHours(-46), Now.AddHours(2)),
            Application("Jerash Rentals", Now.AddHours(-45), Now.AddHours(3))
        };

        var item = Assert.Single(Build(applications: applications).Items);

        Assert.Equal(AttentionQueueBuilder.Kinds.DealerApplicationsAtRisk, item.Kind);
        Assert.Equal(3, item.Count);
        Assert.Equal(3, item.SubjectIds.Count);
        // The row is judged by whichever application breaches first, not by an average.
        Assert.Equal(Now.AddHours(1), item.SlaDeadlineAt);
        Assert.False(item.IsOverdue);
        Assert.Contains("Wadi Rum Motors", item.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void A_freshly_submitted_application_is_not_work_anyone_is_behind_on()
    {
        var fresh = Application("Salt Vehicle Rental", Now.AddHours(-2), Now.AddHours(46));

        Assert.Empty(Build(applications: [fresh]).Items);
    }

    [Fact]
    public void One_breached_application_makes_the_whole_group_row_overdue()
    {
        var applications = new[]
        {
            Application("Jerash Rentals", Now.AddHours(-56), Now.AddHours(-8)),
            Application("Madaba Car Hire", Now.AddHours(-44), Now.AddHours(4))
        };

        var item = Assert.Single(Build(applications: applications).Items);

        Assert.True(item.IsOverdue);
        Assert.Equal(AttentionQueueBuilder.Severities.Overdue, item.Severity);
        Assert.Equal(2, item.Count);
    }

    [Fact]
    public void A_long_group_names_a_few_dealers_and_counts_the_remainder()
    {
        var applications = Enumerable.Range(1, 5)
            .Select(index => Application($"Dealer {index}", Now.AddHours(-47), Now.AddHours(index)))
            .ToArray();

        var item = Assert.Single(Build(applications: applications).Items);

        Assert.Equal(5, item.Count);
        Assert.Contains("and 2 more", item.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dispute_carries_the_label_resolved_for_it_and_never_a_rendered_countdown()
    {
        var dispute = Dispute(Now.AddHours(-60), Now.AddHours(-12));
        var subtitles = new Dictionary<Id, string> { [dispute.TicketId] = "Aqaba Coast Cars . KH-20411" };

        var item = Assert.Single(Build(disputes: [dispute], subtitles: subtitles).Items);

        Assert.Equal("Aqaba Coast Cars . KH-20411", item.Subtitle);
        // Absolute instants, so the console can keep the countdown live between refreshes.
        Assert.Equal(Now.AddHours(-60), item.SlaStartedAt);
        Assert.Equal(Now.AddHours(-12), item.SlaDeadlineAt);
    }

    [Fact]
    public void Counts_reflect_every_row_including_the_grouped_one()
    {
        var disputes = new[]
        {
            Dispute(Now.AddHours(-60), Now.AddHours(-12)),
            Dispute(Now.AddHours(-2), Now.AddHours(46))
        };
        var applications = new[] { Application("Jerash Rentals", Now.AddHours(-56), Now.AddHours(-8)) };

        var result = Build(disputes: disputes, applications: applications);

        Assert.Equal(3, result.Queue.OpenCount);
        Assert.Equal(2, result.Queue.OverdueCount);
    }
}
