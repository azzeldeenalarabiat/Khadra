using System.Text.Json;

namespace Khadra.Tests.Configuration;

/// <summary>
/// What the file that actually ships says.
/// </summary>
/// <remarks>
/// <para>
/// Every other test in this suite proves the code carries a configured number correctly. None of them
/// can prove WHICH number is configured, because they all supply their own. These read
/// <c>Khadra.WebAPI/appsettings.json</c>, which is where the owner's decisions physically live.
/// </para>
/// <para>
/// It also guards a failure mode this project has already had. On 2026-09-11 the entire
/// <c>Payments</c> section was found folded into a single <c>//</c> comment by an editing accident —
/// eight settings including <c>Provider</c>, invisible to the binder. Nothing behaved differently,
/// because the code defaults happened to match, and nothing said a word. Asking for a section by name
/// is what makes that loud: a commented-out section is simply not there.
/// </para>
/// </remarks>
public sealed class ShippedConfigurationTests
{
    private static JsonElement Settings()
    {
        // Anchored on the file itself rather than on the solution, which has been renamed once
        // already (.sln to .slnx) and would have taken this with it.
        var path = Path.Combine("Khadra.WebAPI", "appsettings.json");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, path)))
            root = root.Parent;

        Assert.NotNull(root);

        // JSON-with-comments: nearly every number in the file carries one saying who decided it.
        return JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root.FullName, path)),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }).RootElement.Clone();
    }

    /// <summary>
    /// Two hours to pay after an approval, and a lead time that is a different rule.
    /// </summary>
    /// <remarks>
    /// The owner's decision of 2026-09-11, replacing the twenty-four set on 2026-09-07.
    /// <c>PaymentWindowTests</c> proves the window is carried and enforced; this proves it is two.
    ///
    /// The lead time is asserted to EXIST and not to equal anything. Pinning its value in a
    /// payment-window assertion would mean that the day the owner moves the lead time, the wrong test
    /// fails — and whoever reads that failure edits the number in front of them.
    /// </remarks>
    [Fact]
    public void A_customer_has_two_hours_to_pay_after_an_approval()
    {
        var rules = Settings().GetProperty("BusinessRules");

        Assert.Equal(2, rules.GetProperty("PaymentWindowHours").GetInt32());

        // The RELATIONSHIP between the two, not the lead time's value, which AppConfigTests owns.
        // Since 2026-09-11 an approval must leave the customer their whole payment window, so a lead
        // time no longer than that window means every booking made at the earliest a customer may
        // book for is impossible to approve — and the startup validation refuses to boot on it.
        var leadTime = rules.GetProperty("MinimumBookingLeadTimeMinutes").GetInt32();
        var paymentWindow = rules.GetProperty("PaymentWindowHours").GetInt32() * 60;
        Assert.True(
            leadTime > paymentWindow,
            $"MinimumBookingLeadTimeMinutes ({leadTime}) must exceed the payment window ({paymentWindow} minutes).");
    }

    /// <summary>A hundred saved cars, settled by the owner on 2026-09-11.</summary>
    [Fact]
    public void A_customer_may_save_a_hundred_cars()
    {
        var rules = Settings().GetProperty("BusinessRules");

        Assert.Equal(100, rules.GetProperty("MaxShortlistEntries").GetInt32());
    }

    /// <summary>
    /// There is no payment provider, and the section that says so is really there.
    /// </summary>
    /// <remarks>
    /// Pre-launch item 76, reaffirmed by the owner on 2026-09-11: the provider stays unavailable
    /// until there is a merchant account, and NOBODY may close it with a simulated one. A provider
    /// that captured and confirmed would be indistinguishable, in every table and on every screen,
    /// from a real payment.
    ///
    /// The secrets are asserted EMPTY for a different reason: a real key committed to a tracked file
    /// is a key on the internet, and this is the check that notices.
    /// </remarks>
    [Fact]
    public void No_payment_provider_is_configured_and_no_secret_is_committed()
    {
        var payments = Settings().GetProperty("Payments");

        Assert.Equal("None", payments.GetProperty("Provider").GetString());
        Assert.Equal(string.Empty, payments.GetProperty("ApiKey").GetString());
        Assert.Equal(string.Empty, payments.GetProperty("WebhookSecret").GetString());
    }

    /// <summary>
    /// The document store accepts PDFs, which is why the app offers to send one.
    /// </summary>
    /// <remarks>
    /// The app holds no list of its own: `DocumentPicker` renders whatever this section publishes, so
    /// a type withdrawn here disappears from the sheet without a release. Asserted here because the
    /// reverse is the interesting failure — the app offered only photographs for as long as this said
    /// `application/pdf` and nobody noticed.
    /// </remarks>
    [Fact]
    public void The_document_store_takes_photographs_and_pdfs()
    {
        var documents = Settings().GetProperty("Documents");
        var types = documents.GetProperty("AllowedContentTypes")
            .EnumerateArray()
            .Select(type => type.GetString())
            .ToArray();

        Assert.Contains("application/pdf", types);
        Assert.Contains("image/jpeg", types);
        Assert.True(documents.GetProperty("MaximumSizeBytes").GetInt64() > 0);
    }
}
