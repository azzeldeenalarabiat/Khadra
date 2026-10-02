using System.Net;
using Khadra.Infrastructure.Configuration;
using Khadra.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Security;

/// <summary>
/// Khadra's identity on issued documents (owner, 2026-09-27): all of it or none of it, and a TEST identity
/// only on this machine's sandbox — refused at startup everywhere else, Staging included.
/// </summary>
public sealed class FinancialDocumentConfigurationTests
{
    [Fact]
    public void No_identity_is_valid_and_means_nothing_is_issued()
    {
        var options = new FinancialDocumentOptions();

        Assert.True(new FinancialDocumentIssuerValidator().Validate(null, options).Succeeded);
        Assert.Null(new FinancialDocumentSettings(Options.Create(options)).Issuer);
    }

    [Fact]
    public void A_partial_identity_refuses_to_start_and_names_what_is_missing()
    {
        var options = new FinancialDocumentOptions
        {
            Issuer = new FinancialDocumentIssuerOptions { LegalNameEn = "Khadra LLC", SupportEmail = "support@example.invalid" },
        };

        var result = new FinancialDocumentIssuerValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("FinancialDocuments:Issuer:LegalNameAr", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("FinancialDocuments:Issuer:AddressAr", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("FinancialDocuments:Issuer:LegalNameEn", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_test_flag_without_an_identity_is_partial_too()
    {
        var options = new FinancialDocumentOptions { Issuer = new FinancialDocumentIssuerOptions { TestIdentity = true } };

        Assert.True(new FinancialDocumentIssuerValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void The_support_address_must_be_an_email_address() =>
        Assert.True(new FinancialDocumentIssuerValidator().Validate(null, new FinancialDocumentOptions { Issuer = Complete(supportEmail: "not an address") }).Failed);

    [Fact]
    public void A_complete_identity_is_read_trimmed_in_both_languages()
    {
        var issuer = new FinancialDocumentSettings(Options.Create(new FinancialDocumentOptions { Issuer = Complete(legalNameEn: "  TEST Issuer  ") })).Issuer!;

        Assert.Equal("TEST Issuer", issuer.LegalName.En);
        Assert.Equal("جهة تجريبية", issuer.LegalName.Ar);
        Assert.True(issuer.IsTestIdentity);
    }

    // ── The environment guard ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A test identity starts on a sandbox in Development (owner, 2026-09-27) and on Staging (owner, 2026-10-02,
    /// decision S3): Staging's documents can then be issued, drawn and emailed, every one TEST-numbered.
    /// </summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task A_test_identity_starts_in_Development_and_Staging_on_the_sandbox(string environment)
    {
        using var factory = Api(environment, "SANDBOX");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// And nowhere else: not off the sandbox, not in another environment, and not in Production whatever its
    /// provider — Production's own guards satisfied, so that the refusal below is this one.
    /// </summary>
    [Theory]
    [InlineData("Testing", "SANDBOX")]
    [InlineData("Staging", "None")]
    [InlineData("Development", "None")]
    [InlineData("Production", "None")]
    public async Task A_test_identity_starts_nowhere_else(string environment, string provider)
    {
        using var factory = Api(environment, provider);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        });

        Assert.Contains("FinancialDocuments:Issuer:TestIdentity", failure.Message, StringComparison.Ordinal);
    }

    private static FinancialDocumentIssuerOptions Complete(
        string legalNameEn = "TEST Issuer",
        string supportEmail = "documents@example.invalid") =>
        new()
        {
            LegalNameEn = legalNameEn,
            LegalNameAr = "جهة تجريبية",
            CommercialRegistration = "TEST-0000",
            AddressEn = "Test address",
            AddressAr = "عنوان تجريبي",
            SupportEmail = supportEmail,
            SupportPhone = "+962 6 000 0000",
            TestIdentity = true,
        };

    private static WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> Api(string environment, string provider) =>
        new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.IsolateFromDeveloperDatabase();
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("KnownProxies:0", "10.255.255.1");
                // An appended source outranks everything Program.cs re-adds, the developer's user-secrets
                // and the test process's own environment included (TestHostConfiguration).
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Payments:Provider"] = provider,
                        ["Payments:WebhookSecret"] = "a-sandbox-webhook-secret-for-the-tests",
                        ["Payments:SandboxConsoleBaseUrl"] = "http://192.0.2.10:5012",
                        ["Email:Provider"] = environment == "Production" ? "Resend" : "Logging",
                        ["Email:ApiKey"] = "re_not_a_real_key",
                        ["Email:FromAddress"] = "onboarding@resend.dev",
                        ["Documents:Provider"] = "Supabase",
                        ["Documents:Supabase:Url"] = "https://project.supabase.co",
                        ["Documents:Supabase:Bucket"] = "khadra-documents",
                        ["Documents:Supabase:ServiceKey"] = "not-a-real-key",
                        ["FinancialDocuments:Issuer:LegalNameEn"] = "TEST Issuer",
                        ["FinancialDocuments:Issuer:LegalNameAr"] = "جهة تجريبية",
                        ["FinancialDocuments:Issuer:CommercialRegistration"] = "TEST-0000",
                        ["FinancialDocuments:Issuer:AddressEn"] = "Test address",
                        ["FinancialDocuments:Issuer:AddressAr"] = "عنوان تجريبي",
                        ["FinancialDocuments:Issuer:SupportEmail"] = "documents@example.invalid",
                        ["FinancialDocuments:Issuer:SupportPhone"] = "+962 6 000 0000",
                        ["FinancialDocuments:Issuer:TestIdentity"] = "true",
                    }));
            });
}
