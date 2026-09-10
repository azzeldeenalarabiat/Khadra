using Khadra.Domain.Dealers;

namespace Khadra.Tests.Domain.Dealers;

/// <summary>
/// The address in words that sits beside the map pin.
/// </summary>
/// <remarks>
/// The pin stays the authoritative location — every distance and delivery decision runs on it — and
/// this is what a person reads, because "31.95, 35.91" gets nobody to a door.
///
/// The area is required and the street is not, which is the honest way round for Jordan: "Abdoun"
/// alone gets a customer to the right part of Amman, "Al-Kindi St" alone does not, and a great many
/// streets have no recorded name at all.
/// </remarks>
public sealed class DealerAddressTests
{
    [Fact]
    public void An_area_alone_is_a_complete_address()
    {
        var address = DealerAddress.Create("Abdoun", null);

        Assert.True(address.IsSuccess);
        Assert.Equal("Abdoun", address.Value.Area);
        Assert.Null(address.Value.Street);
    }

    [Fact]
    public void A_street_without_an_area_is_refused()
    {
        var address = DealerAddress.Create(null, "Al-Kindi Street");

        Assert.True(address.IsFailure);
        Assert.Equal("dealer.invalid_address_area", address.Error.Code);
    }

    [Theory]
    [InlineData("  Abdoun  ", "Abdoun")]
    [InlineData("Um   Uthaina", "Um Uthaina")]
    [InlineData("Jabal\tAmman", "Jabal Amman")]
    public void Whitespace_is_tidied_rather_than_preserved(string typed, string stored)
    {
        var address = DealerAddress.Create(typed, null);

        Assert.True(address.IsSuccess);
        Assert.Equal(stored, address.Value.Area);
    }

    /// <summary>A blank street is no street, not a street whose name is the empty string.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_street_is_stored_as_nothing(string? street)
    {
        var address = DealerAddress.Create("Abdoun", street);

        Assert.True(address.IsSuccess);
        Assert.Null(address.Value.Street);
    }

    /// <summary>
    /// Refused, not truncated. A description cut short is still about the same business; a street
    /// name cut short is a different street, and nobody would be told it had happened.
    /// </summary>
    [Fact]
    public void An_over_long_street_is_refused_rather_than_silently_shortened()
    {
        var address = DealerAddress.Create("Abdoun", new string('x', DealerAddress.StreetMaxLength + 1));

        Assert.True(address.IsFailure);
        Assert.Equal("dealer.address_street_too_long", address.Error.Code);
    }

    [Fact]
    public void An_over_long_area_is_refused()
    {
        var address = DealerAddress.Create(new string('x', DealerAddress.AreaMaxLength + 1), null);

        Assert.True(address.IsFailure);
        Assert.Equal("dealer.address_area_too_long", address.Error.Code);
    }

    /// <summary>
    /// A street that IS present but unusable is refused, rather than quietly becoming no street.
    /// Collapsing "absent" and "unusable" would lose something the owner typed without telling them.
    /// </summary>
    [Fact]
    public void A_street_containing_a_control_character_is_refused_not_dropped()
    {
        var address = DealerAddress.Create("Abdoun", "Al-Kindi\u0007Street");

        Assert.True(address.IsFailure);
        Assert.Equal("dealer.invalid_address_street", address.Error.Code);
    }

    /// <summary>Arabic is the platform's other language, and an address may be written in it.</summary>
    [Fact]
    public void An_arabic_address_survives_intact()
    {
        var address = DealerAddress.Create("عبدون", "شارع الكندي");

        Assert.True(address.IsSuccess);
        Assert.Equal("عبدون", address.Value.Area);
        Assert.Equal("شارع الكندي", address.Value.Street);
    }

    /// <summary>
    /// Reading a row never re-litigates whether it should have been allowed in. Tightening a rule
    /// later must not stop an existing gallery's own page loading.
    /// </summary>
    [Fact]
    public void A_stored_address_is_rebuilt_as_it_was_written()
    {
        var address = DealerAddress.FromPersisted(new string('x', 500), "anything at all");

        Assert.Equal(500, address.Area.Length);
    }
}
