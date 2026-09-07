using Khadra.Domain.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Reviews;

/// <summary>
/// The table behind pre-launch checklist item 3.
/// </summary>
/// <remarks>
/// The <c>Review</c> aggregate has existed since the domain model was written and had nowhere to
/// live, so every rating on the platform read null and every screen had to say "no reviews yet".
///
/// A review is NOT soft-deletable, and that is a decision rather than an omission. Spec 4.1 says a
/// dealer's rating is computed from customer reviews and is never editable by the dealer; a delete —
/// even a soft one behind a query filter — is exactly the erasure that rule exists to prevent.
/// Moderation is <c>Hide</c>, which removes the TEXT from display and leaves the score counting.
/// </remarks>
internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> entity)
    {
        ConfigureAggregate(entity, "reviews");

        ConfigureId(entity.Property(review => review.BookingId));
        entity.Property(review => review.BookingId).IsRequired();
        ConfigureEnumeration(entity.Property(review => review.Direction), 30);
        ConfigureId(entity.Property(review => review.ReviewerUserId));
        entity.Property(review => review.ReviewerUserId).IsRequired();
        ConfigureId(entity.Property(review => review.SubjectId));
        entity.Property(review => review.SubjectId).IsRequired();

        // An OWNED type over one column, not a value converter -- and the difference is the whole
        // read model. A converter makes `review.Rating` the column and `review.Rating.Value` an
        // unreadable member access on a converted value, so AVG() and COUNT() over it do not
        // translate at all: EF falls back to loading every review to average them in memory, which is
        // the query the catalogue runs for every card on the screen. Owning it gives `Value` a real
        // column that SQL can aggregate.
        entity.OwnsOne(review => review.Rating, rating =>
            rating.Property(value => value.Value).HasColumnName("rating").IsRequired());
        entity.Navigation(review => review.Rating).IsRequired();

        entity.Property(review => review.Comment).HasMaxLength(2000);
        entity.Property(review => review.IsHidden).IsRequired();
        entity.Property(review => review.HiddenReason).HasMaxLength(500);
        entity.Property(review => review.CreatedAt).IsRequired();

        // One review per booking per direction (spec 5.6). Enforced here rather than only in the
        // handler: two taps on a slow connection are two requests, and the handler's read-then-write
        // loses that race. The unique index is what makes it impossible.
        entity.HasIndex(review => new { review.BookingId, review.Direction }).IsUnique();

        // Every public read is "this gallery's reviews, newest first" and every rating is an average
        // over the same set. Added while the table is empty, which is the only cheap moment.
        entity.HasIndex(review => new { review.SubjectId, review.Direction, review.CreatedAt });
    }
}
