using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Reviews;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Reviews;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Review moderation (pre-launch item 81), against the real EF model on SQLite: what the moderator reads, in both
/// directions, and the hide and restore decisions with the audit entry each one leaves.
/// </summary>
public sealed class ReviewModerationTests : IDisposable
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly Id AdminId = Id.New();

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    private readonly User _customer = Build.Customer();
    private readonly Dealer _office = Build.ApprovedDealer(businessName: "Aqaba Coast Cars");
    private readonly Booking _booking;
    private readonly Review _ofOffice;
    private readonly Review _ofCustomer;

    public ReviewModerationTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;

        _booking = Build.ConfirmedBooking(Now, customerId: _customer.Id, dealerId: _office.Id);

        // The customer's review of the office is published; the office's rating of the customer is still inside its
        // blind window.
        _ofOffice = Review.Leave(
            _booking.Id, ReviewDirection.CustomerRatesDealer, _customer.Id, _office.Id, Rating.Create(1).Value,
            "Call me on 0790000000 — 100%_RUDE", bookingIsCompleted: true, revealAt: Now.AddDays(-1), now: Now.AddDays(-2)).Value;
        _ofCustomer = Review.Leave(
            _booking.Id, ReviewDirection.DealerRatesCustomer, Id.New(), _customer.Id, Rating.Create(2).Value,
            comment: null, bookingIsCompleted: true, revealAt: Now.AddDays(5), now: Now.AddMinutes(-5)).Value;

        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
        context.Users.Add(_customer);
        context.Dealers.Add(_office);
        context.Bookings.Add(_booking);
        context.Reviews.AddRange(_ofOffice, _ofCustomer);
        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private KhadraDbContext NewContext() => new(_options);

    private static Task<PagedResult<ModerationReview>> List(KhadraDbContext context, ReviewModerationFilter? filter = null) =>
        new ReviewModerationReader(context).ListAsync(filter ?? new ReviewModerationFilter(), new PageRequest(1, 20), Now);

    private static ReviewModerationHandlers Handlers(KhadraDbContext context)
    {
        var actor = Substitute.For<ICurrentActor>();
        actor.UserId.Returns(AdminId);
        actor.Role.Returns(UserRole.Admin);
        actor.Name.Returns("Rania Haddad");
        actor.CorrelationId.Returns("moderation-test");
        return new ReviewModerationHandlers(
            new ReviewRepository(context),
            new ReviewModerationReader(context),
            new AdminActionRecorder(new AuditTrail(context), actor, new TestClock(Now)),
            new TestClock(Now),
            new UnitOfWork(context, Substitute.For<IDomainEventDispatcher>()));
    }

    [Fact]
    public async Task Both_directions_are_listed_newest_first_with_who_wrote_them_about_whom()
    {
        await using var context = NewContext();

        var page = await List(context);

        Assert.Equal(2, page.TotalCount);
        var (ofCustomer, ofOffice) = (page.Items[0], page.Items[1]);

        Assert.Equal(_ofCustomer.Id.Value, ofCustomer.ReviewId);
        Assert.Equal(nameof(ReviewDirection.DealerRatesCustomer), ofCustomer.Direction);
        Assert.Equal(_office.Id.Value, ofCustomer.ReviewerId);
        Assert.Equal("Aqaba Coast Cars", ofCustomer.ReviewerName);
        Assert.Equal(_customer.Id.Value, ofCustomer.SubjectId);
        Assert.Equal("Rana Sharif", ofCustomer.SubjectName);
        Assert.False(ofCustomer.IsPublished);

        Assert.Equal(_customer.Id.Value, ofOffice.ReviewerId);
        Assert.Equal("Rana Sharif", ofOffice.ReviewerName);
        Assert.Equal(_office.Id.Value, ofOffice.SubjectId);
        Assert.Equal("Aqaba Coast Cars", ofOffice.SubjectName);
        Assert.Equal(_booking.Reference.Value, ofOffice.BookingReference);
        Assert.True(ofOffice.IsPublished);
        // The moderator reads the whole comment: judging it is the point.
        Assert.Equal("Call me on 0790000000 — 100%_RUDE", ofOffice.Comment);
    }

    [Fact]
    public async Task The_filters_compose_and_a_search_reads_the_comment_or_an_exact_reference()
    {
        await using var context = NewContext();

        Assert.Single((await List(context, new(Direction: ReviewDirection.CustomerRatesDealer))).Items);
        Assert.Single((await List(context, new(Rating: 2))).Items);
        Assert.Empty((await List(context, new(Hidden: true))).Items);
        Assert.Equal(2, (await List(context, new(Hidden: false))).TotalCount);

        // Case-insensitive, and the LIKE wildcards in what was typed are literal.
        Assert.Single((await List(context, new(Search: "100%_rude"))).Items);
        Assert.Empty((await List(context, new(Search: "100%xrude"))).Items);
        // A booking's reference matches both of its reviews, the one with no comment included.
        Assert.Equal(2, (await List(context, new(Search: _booking.Reference.Value.ToLowerInvariant()))).TotalCount);
    }

    [Fact]
    public async Task Hiding_a_review_is_audited_with_the_booking_and_the_reason_as_a_code()
    {
        await using (var context = NewContext())
        {
            var hidden = await Handlers(context).Handle(
                new HideReviewCommand(_ofOffice.Id, nameof(ReviewHideReason.PersonalContactDetails)), CancellationToken.None);

            Assert.True(hidden.IsSuccess);
            Assert.True(hidden.Value.IsHidden);
            Assert.Equal(nameof(ReviewHideReason.PersonalContactDetails), hidden.Value.HiddenReason);
        }

        await using var check = NewContext();
        var entry = await check.AuditEntries.SingleAsync();
        Assert.Equal(AuditAction.ReviewHidden, entry.Action);
        Assert.Equal(AuditEntityType.Review, entry.EntityType);
        Assert.Equal(_ofOffice.Id, entry.EntityId);
        Assert.Equal(AdminId, entry.ActorUserId);
        // A reference, never a person's name: the table can never be rewritten.
        Assert.Equal(_booking.Reference.Value, entry.SubjectLabel);
        Assert.Null(entry.PreviousValue);
        Assert.Equal(nameof(ReviewHideReason.PersonalContactDetails), entry.NewValue);
        Assert.Null(entry.Reason);
        Assert.Single((await List(check, new(Hidden: true))).Items);
    }

    [Fact]
    public async Task Restoring_moves_the_reason_to_the_previous_value_and_a_second_click_is_refused()
    {
        await using (var context = NewContext())
        {
            var handlers = Handlers(context);
            await handlers.Handle(new HideReviewCommand(_ofCustomer.Id, nameof(ReviewHideReason.NotAboutThisRental)), CancellationToken.None);

            var again = await handlers.Handle(
                new HideReviewCommand(_ofCustomer.Id, nameof(ReviewHideReason.SpamOrPromotion)), CancellationToken.None);
            Assert.Equal(ReviewErrors.AlreadyHidden, again.Error);

            var restored = await handlers.Handle(new RestoreReviewCommand(_ofCustomer.Id), CancellationToken.None);
            Assert.True(restored.IsSuccess);
            Assert.False(restored.Value.IsHidden);
            Assert.Null(restored.Value.HiddenReason);

            Assert.Equal(ReviewErrors.NotHidden, (await handlers.Handle(new RestoreReviewCommand(_ofCustomer.Id), CancellationToken.None)).Error);
        }

        await using var check = NewContext();
        var entries = await check.AuditEntries.OrderBy(entry => entry.Action).ToListAsync();
        Assert.Equal(2, entries.Count);
        var restore = entries.Single(entry => entry.Action == AuditAction.ReviewRestored);
        Assert.Equal(nameof(ReviewHideReason.NotAboutThisRental), restore.PreviousValue);
        Assert.Null(restore.NewValue);
    }

    [Fact]
    public async Task An_unknown_review_is_not_found()
    {
        await using var context = NewContext();

        var result = await Handlers(context).Handle(new RestoreReviewCommand(Id.New()), CancellationToken.None);

        Assert.Equal(ReviewErrors.NotFound, result.Error);
        Assert.Empty(await context.AuditEntries.ToListAsync());
    }

    [Fact]
    public void A_reason_outside_the_policy_list_is_refused()
    {
        var validator = new HideReviewCommandValidator();

        Assert.True(validator.Validate(new HideReviewCommand(Id.New(), nameof(ReviewHideReason.AbusiveLanguage))).IsValid);
        Assert.False(validator.Validate(new HideReviewCommand(Id.New(), "Because I said so")).IsValid);
    }
}
