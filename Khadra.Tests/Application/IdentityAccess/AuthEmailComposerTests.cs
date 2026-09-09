using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Notifications;
using Khadra.Tests.Support;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Application.IdentityAccess;

/// <summary>
/// The five account emails, in both languages.
/// </summary>
/// <remarks>
/// Until 2026-09-08 every one of them left in English only, on a Jordanian marketplace — including
/// the verification link, which is the one email a customer MUST act on before they can do anything
/// at all. Pre-launch item 40.
///
/// The cases are named by STRING rather than by passing a delegate, because `AuthEmailComposer` is
/// internal and cannot appear in a public test signature. The switch below is the whole cost of that.
/// </remarks>
public sealed class AuthEmailComposerTests
{
    // The concrete type, not the port: this tests the templates themselves, and a field typed as the
    // interface is what CA1859 objects to.
    private static readonly AuthEmailComposer Composer =
        new(Options.Create(new AppOptions { ClientBaseUrl = "https://app.khadra.test/" }));

    private static EmailMessage Compose(string which) => which switch
    {
        "verification" => Composer.EmailVerification(Build.Customer(), "tok-1"),
        "password reset" => Composer.PasswordReset(Build.Customer(), "tok-2"),
        "password changed" => Composer.PasswordChanged(Build.Customer()),
        "admin invitation" => Composer.AdminInvitation(Build.Customer(), "tok-3"),
        "staff invitation" => Composer.EmployeeInvitation(Build.Customer(), "Petra Rentals", "tok-4"),
        _ => throw new ArgumentOutOfRangeException(nameof(which), which, "no such message"),
    };

    private static bool HasArabic(string text) => text.Any(character => character is >= '؀' and <= 'ۿ');

    [Theory]
    [InlineData("verification")]
    [InlineData("password reset")]
    [InlineData("password changed")]
    [InlineData("admin invitation")]
    [InlineData("staff invitation")]
    public void Every_account_email_carries_both_languages(string which)
    {
        var message = Compose(which);

        Assert.True(HasArabic(message.Subject), $"{which}: the subject line has no Arabic");
        Assert.True(HasArabic(message.HtmlBody), $"{which}: the HTML body has no Arabic");
        // The PLAIN TEXT part too. A client that refuses HTML sees only this, and so does every spam
        // filter deciding whether the message is worth delivering.
        Assert.True(HasArabic(message.TextBody), $"{which}: the text body has no Arabic");

        Assert.Contains("Khadra", message.Subject, StringComparison.Ordinal);
        Assert.Contains("Hi ", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Hi ", message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// The direction is on the Arabic BLOCK, never on the document.
    /// </summary>
    /// <remarks>
    /// A direction on the body would flip the English half below it, taking its punctuation and its
    /// link text with it.
    /// </remarks>
    [Theory]
    [InlineData("verification")]
    [InlineData("password reset")]
    [InlineData("password changed")]
    [InlineData("admin invitation")]
    [InlineData("staff invitation")]
    public void The_arabic_half_is_right_to_left_and_comes_first(string which)
    {
        var html = Compose(which).HtmlBody;

        var rtl = html.IndexOf("dir=\"rtl\"", StringComparison.Ordinal);
        var ltr = html.IndexOf("dir=\"ltr\"", StringComparison.Ordinal);

        Assert.True(rtl >= 0, $"{which}: no right-to-left block");
        Assert.True(ltr >= 0, $"{which}: no left-to-right block");
        Assert.True(rtl < ltr, $"{which}: Arabic should come first on a Jordanian platform");
    }

    /// <summary>
    /// A single-use token appears exactly ONCE PER LANGUAGE, in both parts.
    /// </summary>
    /// <remarks>
    /// Two links to one token in the SAME half would invite clicking the second after the first had
    /// consumed it, and reading the failure as the platform being broken rather than as the link
    /// having worked. Once in each half is the bilingual message doing its job: each block is
    /// self-contained, so a reader never has to look at the language they do not read to find the
    /// thing they came to press.
    /// </remarks>
    [Fact]
    public void The_link_appears_once_per_language()
    {
        var message = Compose("verification");
        const string link = "https://app.khadra.test/verify-email?token=tok-1";

        Assert.Equal(2, Occurrences(message.HtmlBody, link));
        Assert.Equal(2, Occurrences(message.TextBody, link));
    }

    /// <summary>A message with nothing to click carries no link, and no empty anchor either.</summary>
    [Fact]
    public void The_password_changed_notice_has_no_action()
    {
        var message = Compose("password changed");

        Assert.DoesNotContain("<a href", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("https://app.khadra.test", message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// A gallery's name reaches the HTML encoded.
    /// </summary>
    /// <remarks>
    /// A business name is attacker-controlled — it is typed into the registration form — and an
    /// inbox is one of the few places this platform emits HTML it did not write itself.
    /// </remarks>
    [Fact]
    public void A_business_name_is_encoded_before_it_reaches_the_html()
    {
        var message = Composer.EmployeeInvitation(
            Build.Customer(), "Petra <script>alert(1)</script> Rentals", "tok-4");

        Assert.DoesNotContain("<script>", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", message.HtmlBody, StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var at = haystack.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
