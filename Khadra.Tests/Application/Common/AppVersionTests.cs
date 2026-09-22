using System.Text.Json;
using Khadra.Application.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.Common;

/// <summary>
/// How two customer-app versions are ordered, on the server's side of the version gate.
/// </summary>
/// <remarks>
/// The cases live in <c>docs/contracts/app-version-vectors.json</c>, and the app's twin of this type
/// is tested against the SAME file (<c>Khadra.Mobile/test/app_version_test.dart</c>). Two lists typed
/// out twice would agree today and drift the first time one side learned a new case — and the gate is
/// only as good as the two sides agreeing about which build is newer.
/// </remarks>
public sealed class AppVersionTests
{
    private static readonly JsonElement Vectors =
        JsonDocument.Parse(File.ReadAllText(RepositoryRoot.File("docs", "contracts", "app-version-vectors.json"))).RootElement;

    private static AppVersion Parse(string text)
    {
        Assert.True(AppVersion.TryParse(text, out var version), $"\"{text}\" should parse.");
        return version;
    }

    public static TheoryData<string[]> Ascending()
    {
        var data = new TheoryData<string[]>();
        foreach (var list in Vectors.GetProperty("ascending").EnumerateArray())
            data.Add([.. list.EnumerateArray().Select(item => item.GetString()!)]);
        return data;
    }

    public static TheoryData<string, string> Equal()
    {
        var data = new TheoryData<string, string>();
        foreach (var pair in Vectors.GetProperty("equal").EnumerateArray())
            data.Add(pair[0].GetString()!, pair[1].GetString()!);
        return data;
    }

    public static TheoryData<string> Invalid()
    {
        var data = new TheoryData<string>();
        foreach (var text in Vectors.GetProperty("invalid").EnumerateArray())
            data.Add(text.GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Ascending))]
    public void Every_pair_in_an_ascending_list_is_ordered_both_ways(string[] ascending)
    {
        // Every pair, not only neighbours: an ordering that is only right between adjacent entries is
        // not an ordering, and the gate compares arbitrary builds against an arbitrary minimum.
        for (var lower = 0; lower < ascending.Length; lower++)
        {
            for (var higher = lower + 1; higher < ascending.Length; higher++)
            {
                var (a, b) = (Parse(ascending[lower]), Parse(ascending[higher]));
                Assert.True(a < b, $"{ascending[lower]} should be below {ascending[higher]}.");
                Assert.True(b > a, $"{ascending[higher]} should be above {ascending[lower]}.");
                Assert.True(a.CompareTo(b) < 0 && b.CompareTo(a) > 0);
                Assert.NotEqual(a, b);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Equal))]
    public void Build_metadata_never_changes_the_order(string left, string right)
    {
        var (a, b) = (Parse(left), Parse(right));

        Assert.Equal(0, a.CompareTo(b));
        Assert.True(a <= b && a >= b);
        // And equal as values too, so a set or a dictionary cannot disagree with the comparison.
        Assert.Equal(a, b);
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Anything_that_is_not_a_semantic_version_is_refused(string text) =>
        Assert.False(AppVersion.TryParse(text, out _), $"\"{text}\" should not parse.");

    [Fact]
    public void Numbers_are_compared_as_numbers_not_as_text()
    {
        // The whole reason this type exists. As strings "1.10.0" < "1.9.0", and a minimum of 1.9.0
        // would have locked out every build from 1.10 onwards.
        Assert.True(string.CompareOrdinal("1.10.0", "1.9.0") < 0);
        Assert.True(Parse("1.10.0") > Parse("1.9.0"));
    }

    [Fact]
    public void A_version_prints_without_its_build_metadata()
    {
        Assert.Equal("1.1.0", Parse("1.1.0+2").ToString());
        Assert.Equal("1.1.0-rc.1", Parse("1.1.0-rc.1+7").ToString());
        Assert.False(AppVersion.TryParse(null, out _));
    }
}
