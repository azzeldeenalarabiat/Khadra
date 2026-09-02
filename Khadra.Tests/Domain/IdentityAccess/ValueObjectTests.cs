using Khadra.Domain.IdentityAccess;

namespace Khadra.Tests.Domain.IdentityAccess;

public sealed class EmailAddressTests
{
    [Fact]
    public void Normalises_case_and_whitespace()
    {
        var email = EmailAddress.Create("  Ali@Example.COM ");

        Assert.True(email.IsSuccess);
        Assert.Equal("ali@example.com", email.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing@tld")]
    [InlineData("spaces in@example.com")]
    public void Rejects_malformed_addresses(string? raw)
    {
        var email = EmailAddress.Create(raw);

        Assert.True(email.IsFailure);
        Assert.Equal("auth.invalid_email", email.Error.Code);
    }

    [Fact]
    public void Rejects_addresses_over_the_maximum_length()
    {
        var raw = new string('a', 250) + "@example.com";

        Assert.True(EmailAddress.Create(raw).IsFailure);
    }

    [Fact]
    public void Equality_is_by_normalised_value()
    {
        Assert.Equal(EmailAddress.Create("A@b.co").Value, EmailAddress.Create("a@B.CO").Value);
    }
}

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("0791234567", "+962791234567")]
    [InlineData("079-123-4567", "+962791234567")]
    [InlineData("+962 79 123 4567", "+962791234567")]
    [InlineData("00962791234567", "+962791234567")]
    [InlineData("962791234567", "+962791234567")]
    [InlineData("(078) 123 4567", "+962781234567")]
    public void Normalises_jordanian_mobiles_to_e164(string raw, string expected)
    {
        var phone = PhoneNumber.Create(raw);

        Assert.True(phone.IsSuccess);
        Assert.Equal(expected, phone.Value.Value);
        Assert.True(phone.Value.IsJordanian);
    }

    [Fact]
    public void Accepts_foreign_numbers_with_a_country_code()
    {
        var phone = PhoneNumber.Create("+1 415 555 2671");

        Assert.True(phone.IsSuccess);
        Assert.Equal("+14155552671", phone.Value.Value);
        Assert.False(phone.Value.IsJordanian);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("0761234567")]
    [InlineData("+96261234567")]
    [InlineData("4155552671")]
    [InlineData("+0123456789")]
    public void Rejects_invalid_or_ambiguous_numbers(string? raw)
    {
        var phone = PhoneNumber.Create(raw);

        Assert.True(phone.IsFailure);
        Assert.Equal("auth.invalid_phone", phone.Error.Code);
    }
}

public sealed class PersonNameTests
{
    [Fact]
    public void Trims_and_collapses_whitespace()
    {
        var name = PersonName.Create("  Ali   Ahmad  ");

        Assert.True(name.IsSuccess);
        Assert.Equal("Ali Ahmad", name.Value.Value);
    }

    [Fact]
    public void Accepts_arabic_names()
    {
        Assert.True(PersonName.Create("علي أحمد").IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Ali\tAhmad")]
    public void Rejects_too_short_or_control_characters(string raw)
    {
        Assert.True(PersonName.Create(raw).IsFailure);
    }

    [Fact]
    public void Rejects_names_over_the_maximum_length()
    {
        Assert.True(PersonName.Create(new string('x', 151)).IsFailure);
    }
}

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("", "required")]
    [InlineData("Ab1", "at least 8")]
    [InlineData("12345678", "letter")]
    [InlineData("abcdefgh", "digit")]
    [InlineData("abcd 1234", "spaces")]
    public void Rejects_weak_passwords_with_a_reason(string password, string reasonFragment)
    {
        var result = PasswordPolicy.Validate(password, 8);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.password_policy", result.Error.Code);
        Assert.Contains(reasonFragment, result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_passwords_over_the_bcrypt_limit()
    {
        Assert.True(PasswordPolicy.Validate(new string('a', 72) + "1", 8).IsFailure);
    }

    [Fact]
    public void Honours_a_configured_minimum_but_never_below_eight()
    {
        Assert.True(PasswordPolicy.Validate("abcdef12", 12).IsFailure);
        Assert.True(PasswordPolicy.Validate("abcdef12", 4).IsSuccess);
        Assert.True(PasswordPolicy.Validate("abcdefghijk12", 12).IsSuccess);
    }
}
