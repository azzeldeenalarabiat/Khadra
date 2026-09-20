using Khadra.Domain.Dealers;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Khadra.Infrastructure.Persistence.Configurations.Dealers;

// The sections an office has hidden, stored as one short string of their NAMES:
//
//   About;Insurance
//
// Names rather than ids, so the column reads plainly in psql and a section can be looked for by eye.
// Nothing queries into it: the page is built from the aggregate.
//
// The read is total. A name this build does not know — one retired from the vocabulary, or a typo put
// in by hand — is DROPPED rather than thrown, because materialising a dealer must never fail over a
// stored string, and a section that no longer exists has nothing left to hide.
internal static class HiddenSectionsConverter
{
    /// <summary>Room for every section at once, with space to grow.</summary>
    public const int MaxLength = 200;

    public static readonly ValueConverter<IReadOnlySet<PublicProfileSection>, string> Instance =
        new(sections => Write(sections), value => Read(value));

    // Compared by content: the domain hands over a new set on every save, and reference equality would
    // mark every loaded dealer as modified.
    public static readonly ValueComparer<IReadOnlySet<PublicProfileSection>> Comparer =
        new((left, right) => ReferenceEquals(left, right) || (left != null && right != null && left.SetEquals(right)),
            sections => sections.Aggregate(0, (hash, section) => hash ^ section.GetHashCode()),
            sections => sections.ToHashSet());

    private static string Write(IReadOnlySet<PublicProfileSection> sections) =>
        string.Join(';', sections.OrderBy(section => section.Id).Select(section => section.Name));

    private static HashSet<PublicProfileSection> Read(string value) =>
        value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(PublicProfileSection.FromNameOrNull)
            .OfType<PublicProfileSection>()
            .ToHashSet();
}
