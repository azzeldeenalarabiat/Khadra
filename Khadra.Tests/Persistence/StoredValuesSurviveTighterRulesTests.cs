using Khadra.Domain.Bookings;
using Khadra.Domain.Dealers;
using Khadra.Domain.Fleet;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Reading a row is not the moment to re-litigate whether it should have been allowed in.
/// </summary>
/// <remarks>
/// The EF converters rebuilt every one of these through <c>Create(...).Value</c>, and <c>.Value</c>
/// on a failed result throws. Nothing was broken while every stored value happened to satisfy the
/// current rule — but the rules do move: the commercial registration one was tightened on
/// 2026-09-06 so that letters are refused rather than silently deleted. Had any stored row contained
/// a letter, that change would have turned every dealer query into an exception on load, not a
/// validation failure a caller could handle.
///
/// These tests use values the WRITE path refuses on purpose. If someone later routes a converter
/// back through <c>Create</c>, they fail here rather than in production on the next rule change.
/// </remarks>
public sealed class StoredValuesSurviveTighterRulesTests
{
    [Fact]
    public void A_registration_with_letters_still_loads_though_it_could_not_be_written_today()
    {
        // Exactly the shape the old strip-everything behaviour could leave behind.
        const string stored = "AB1234";
        Assert.True(CommercialRegistrationNumber.Create(stored).IsFailure);

        var loaded = CommercialRegistrationNumber.FromPersisted(stored);

        Assert.Equal(stored, loaded.Value);
    }

    [Fact]
    public void A_plate_with_letters_still_loads()
    {
        const string stored = "AB1234";
        Assert.True(PlateNumber.Create(stored).IsFailure);

        Assert.Equal(stored, PlateNumber.FromPersisted(stored).Value);
    }

    [Fact]
    public void A_name_shorter_than_the_rule_allows_still_loads()
    {
        const string stored = "A";
        Assert.True(PersonName.Create(stored).IsFailure);

        Assert.Equal(stored, PersonName.FromPersisted(stored).Value);
    }

    [Fact]
    public void An_address_the_rule_would_refuse_still_loads()
    {
        const string stored = "not-an-email";
        Assert.True(EmailAddress.Create(stored).IsFailure);

        Assert.Equal(stored, EmailAddress.FromPersisted(stored).Value);
    }

    [Fact]
    public void A_phone_the_rule_would_refuse_still_loads()
    {
        const string stored = "12345";
        Assert.True(PhoneNumber.Create(stored).IsFailure);

        Assert.Equal(stored, PhoneNumber.FromPersisted(stored).Value);
    }

    [Fact]
    public void A_business_name_shorter_than_the_rule_allows_still_loads()
    {
        const string stored = "X";
        Assert.True(BusinessName.Create(stored).IsFailure);

        Assert.Equal(stored, BusinessName.FromPersisted(stored).Value);
    }

    [Fact]
    public void A_booking_reference_keeps_whatever_was_stored()
    {
        const string stored = "KH-LEGACY-01";

        Assert.Equal(stored, BookingReference.FromPersisted(stored).Value);
    }

    /// <summary>
    /// The other half: loading a value leniently must not make the WRITE path lenient too. The rule
    /// still lives in Create, which is the only door new values come through.
    /// </summary>
    [Fact]
    public void The_write_path_is_unchanged()
    {
        Assert.True(CommercialRegistrationNumber.Create("AB1234").IsFailure);
        Assert.True(PlateNumber.Create("AB1234").IsFailure);
        Assert.True(PersonName.Create("A").IsFailure);
        Assert.True(EmailAddress.Create("not-an-email").IsFailure);

        Assert.Equal("123456", CommercialRegistrationNumber.Create("123-456").Value.Value);
    }
}
