using Khadra.Application.Auditing.ReadAuditLog;
using Khadra.Application.Auditing.ReadModels;
using Khadra.Application.Common;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Auditing;

// The audit log's read path. The interesting logic is not the query -- it is what a date an admin
// typed into a picker actually means.
public sealed class AuditLogQueryTests
{
    private readonly IAuditLogReader _reader = Substitute.For<IAuditLogReader>();

    private ListAuditLogHandler Handler()
    {
        _reader.ListAsync(Arg.Any<AuditLogFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(PagedResult.Empty<AuditLogEntry>(1, 25));

        return new ListAuditLogHandler(_reader, TestBusinessRules.Calendar());
    }

    private async Task<AuditLogFilter> FilterFor(ListAuditLogQuery query)
    {
        await Handler().Handle(query, CancellationToken.None);
        return _reader.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IAuditLogReader.ListAsync))
            .GetArguments()[0] as AuditLogFilter
            ?? throw new InvalidOperationException("The reader was not asked for a filter.");
    }

    private static ListAuditLogQuery Query(DateOnly? from = null, DateOnly? to = null) =>
        new(null, null, null, null, from, to, null, null, null);

    /// <summary>
    /// "To 3 September" means the whole of 3 September.
    ///
    /// The obvious reading — compare against the date itself — resolves to midnight at its START, so
    /// everything that happened during the day an admin explicitly asked about is silently excluded.
    /// On a log used to reconstruct what happened on a given day, that is the difference between
    /// "nobody did anything" and "we did not look".
    /// </summary>
    [Fact]
    public async Task The_end_of_the_range_includes_the_whole_day_it_names()
    {
        var filter = await FilterFor(Query(to: new DateOnly(2026, 9, 3)));

        // Local midnight in Amman (UTC+3) at the start of 4 September, exclusive -- so every instant
        // belonging to 3 September local is inside the window.
        Assert.Equal(
            new DateTimeOffset(2026, 9, 3, 21, 0, 0, TimeSpan.Zero),
            filter.OccurredBefore);
    }

    /// <summary>
    /// The dates are calendar days in the platform's reporting zone, not UTC.
    ///
    /// An admin in Amman asking for "3 September" means their 3 September. Resolving it in UTC would
    /// shift the window three hours and quietly move the first and last actions of every day into the
    /// neighbouring one.
    /// </summary>
    [Fact]
    public async Task The_start_of_the_range_is_local_midnight_not_utc_midnight()
    {
        var filter = await FilterFor(Query(from: new DateOnly(2026, 9, 3)));

        Assert.Equal(
            new DateTimeOffset(2026, 9, 2, 21, 0, 0, TimeSpan.Zero),
            filter.OccurredFrom);
    }

    [Fact]
    public async Task An_absent_date_leaves_that_end_of_the_window_open()
    {
        var filter = await FilterFor(Query());

        Assert.Null(filter.OccurredFrom);
        Assert.Null(filter.OccurredBefore);
    }

    [Theory]
    [InlineData("NoSuchAction", null)]
    [InlineData(null, "NoSuchEntity")]
    public void An_unknown_filter_value_is_rejected_rather_than_ignored(string? action, string? entityType)
    {
        var validator = new ListAuditLogQueryValidator();

        var result = validator.Validate(new ListAuditLogQuery(
            action, entityType, null, null, null, null, null, null, null));

        // Rejected, not dropped. A filter that silently does nothing lets an auditor believe they
        // have seen everything of a kind when they are looking at the unfiltered log.
        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_range_that_ends_before_it_starts_is_rejected()
    {
        var validator = new ListAuditLogQueryValidator();

        var result = validator.Validate(Query(
            from: new DateOnly(2026, 9, 5),
            to: new DateOnly(2026, 9, 1)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Every_action_and_entity_type_the_domain_declares_is_accepted()
    {
        var validator = new ListAuditLogQueryValidator();

        // The vocabulary endpoint offers these, so the validator must accept every one of them --
        // otherwise the screen presents a filter that its own API rejects.
        foreach (var action in Khadra.Domain.Common.Enumeration.GetAll<Khadra.Domain.Auditing.AuditAction>())
        {
            var result = validator.Validate(new ListAuditLogQuery(
                action.Name, null, null, null, null, null, null, null, null));
            Assert.True(result.IsValid, $"The validator rejected {action.Name}, which the vocabulary offers.");
        }

        foreach (var entityType in Khadra.Domain.Common.Enumeration.GetAll<Khadra.Domain.Auditing.AuditEntityType>())
        {
            var result = validator.Validate(new ListAuditLogQuery(
                null, entityType.Name, null, null, null, null, null, null, null));
            Assert.True(result.IsValid, $"The validator rejected {entityType.Name}, which the vocabulary offers.");
        }
    }
}
