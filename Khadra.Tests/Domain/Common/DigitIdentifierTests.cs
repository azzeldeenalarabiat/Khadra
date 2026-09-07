using Khadra.Domain.Common;
using Khadra.Domain.Fleet;

namespace Khadra.Tests.Domain.Common;

/// <summary>
/// Both identifiers this normalises sit behind a UNIQUE index, which is what turned "delete the
/// characters I do not recognise" from untidy into unsafe: two different numbers could normalise
/// onto one another, and the second person to type theirs was refused for a number that was never
/// theirs. Pinned here because the rule has to stay the same for both.
/// </summary>
public sealed class DigitIdentifierTests
{
    [Theory]
    [InlineData("123456", "123456")]
    [InlineData("12 34 56", "123456")]
    [InlineData("12-34-56", "123456")]
    [InlineData("12/34/56", "123456")]
    [InlineData("12.34.56", "123456")]
    public void Drops_the_separators_a_person_would_type(string raw, string expected) =>
        Assert.Equal(expected, DigitIdentifier.Normalise(raw));

    [Theory]
    [InlineData("AB1234")]
    [InlineData("E2E20260906")]
    [InlineData("1234x")]
    [InlineData("١٢٣٤")]      // Arabic-Indic digits: not ASCII, so not silently reinterpreted
    [InlineData("12,34")]     // a comma is not one of the separators people use for these
    public void Refuses_anything_else_instead_of_deleting_it(string raw) =>
        Assert.Null(DigitIdentifier.Normalise(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void Has_nothing_to_return_for_an_empty_value(string? raw) =>
        Assert.Null(DigitIdentifier.Normalise(raw));

    /// <summary>
    /// The collision the old behaviour allowed, stated as a test: these two are different plates and
    /// must stay different, where before both became "1234".
    /// </summary>
    [Fact]
    public void Two_plates_that_used_to_collide_no_longer_do()
    {
        Assert.True(PlateNumber.Create("AB1234").IsFailure);
        Assert.Equal("1234", PlateNumber.Create("1234").Value.Value);
    }
}
