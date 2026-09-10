using Khadra.Application.Common.Ports;
using Khadra.Infrastructure;
using Khadra.Infrastructure.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Khadra.Tests.Security;

/// <summary>
/// Which store a given configuration selects, and which configurations are refused at startup.
/// </summary>
/// <remarks>
/// The stakes are higher here than for the mail transport, and in the same shape. A silently wrong
/// mail provider delivers nothing; a silently wrong document store accepts every upload, reports
/// success, and deletes the lot on the next deploy — taking with it the licence scans an
/// administrator approved a business against, and identity papers the platform is trusted to hold.
///
/// So: an unrecognised provider is refused, and Supabase without its settings is refused, both at
/// boot rather than at the first upload. The first upload is the worst possible moment to find out,
/// because by then somebody has filled in a whole application form.
/// </remarks>
public sealed class DocumentStorageConfigurationTests
{
    private static ServiceProvider Resolve(params (string Key, string? Value)[] settings)
    {
        var baseline = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=khadra_tests;Username=x;Password=y",
            ["Authentication:Jwt:SigningKey"] = new string('k', 48),
            ["Documents:AllowedContentTypes:0"] = "image/jpeg",
        };
        foreach (var (key, value) in settings) baseline[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(baseline).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment
            {
                ContentRootPath = Path.GetTempPath(),
                EnvironmentName = "Testing",
                ApplicationName = "Khadra.Tests",
            });
        services.AddInfrastructureServices(configuration);
        return services.BuildServiceProvider();
    }

    private static (string, string?)[] Supabase(params (string, string?)[] overrides)
    {
        var settings = new List<(string, string?)>
        {
            ("Documents:Provider", "Supabase"),
            ("Documents:Supabase:Url", "https://project.supabase.co"),
            ("Documents:Supabase:Bucket", "khadra-documents"),
            ("Documents:Supabase:ServiceKey", "not-a-real-key"),
        };
        foreach (var (key, value) in overrides)
        {
            settings.RemoveAll(entry => entry.Item1 == key);
            settings.Add((key, value));
        }

        return [.. settings];
    }

    /// <summary>Local is the default, so development and the tests need no configuration at all.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Local")]
    [InlineData("local")]
    public void Nothing_configured_keeps_documents_on_this_machine(string? provider) =>
        Assert.IsType<LocalDocumentStorage>(
            Resolve(("Documents:Provider", provider)).GetRequiredService<IDocumentStorage>());

    [Theory]
    [InlineData("Supabase")]
    [InlineData("supabase")]
    [InlineData("  Supabase  ")]
    public void Supabase_is_selected_whatever_the_case_or_spacing(string provider) =>
        Assert.IsType<SupabaseDocumentStorage>(
            Resolve(Supabase(("Documents:Provider", provider))
                .Select(entry => (entry.Item1, entry.Item2)).ToArray())
                .GetRequiredService<IDocumentStorage>());

    /// <summary>
    /// Each missing setting is NAMED. "Supabase storage is misconfigured" sends somebody hunting
    /// through four settings; naming the one that is missing ends the search.
    /// </summary>
    [Theory]
    [InlineData("Documents:Supabase:Url")]
    [InlineData("Documents:Supabase:Bucket")]
    [InlineData("Documents:Supabase:ServiceKey")]
    public void Supabase_without_all_of_its_settings_refuses_to_start(string missing)
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Resolve(Supabase((missing, null))));

        Assert.Contains(missing, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_url_that_is_not_a_url_refuses_to_start()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Resolve(Supabase(("Documents:Supabase:Url", "project.supabase.co"))));

        Assert.Contains("absolute URL", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The regression the mail transport taught. An unrecognised name must not quietly choose the
    /// store that loses everything on redeploy — which is what a silent fallback to Local would be.
    /// </summary>
    [Theory]
    [InlineData("\"Supabase\"")]
    [InlineData("S3")]
    [InlineData("Supabse")]
    public void An_unrecognised_store_is_refused_and_names_itself(string provider)
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Resolve(("Documents:Provider", provider)));

        Assert.Contains(provider, failure.Message, StringComparison.Ordinal);
        Assert.Contains("Local", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Production must not keep documents on the container's own disk.
    /// </summary>
    /// <remarks>
    /// Local is the DEFAULT, so a forgotten variable does not fail — it accepts every upload, reports
    /// success, and deletes the lot on the next deploy, taking licence scans an administrator approved
    /// a business against and identity papers the platform was trusted to hold. Render leaves
    /// ASPNETCORE_ENVIRONMENT unset, which defaults to Production, so this bites exactly where it must.
    /// </remarks>
    [Theory]
    [InlineData("Local")]
    [InlineData(null)]
    public async Task Production_refuses_to_keep_documents_on_the_container(string? provider)
    {
        using var factory = new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=khadra_tests;Username=x;Password=y");
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Database:AutoMigrate", "false");
                builder.UseSetting("KnownProxies:0", "10.255.255.1");
                // Appended last so an untracked appsettings.Local.json cannot decide this test.
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        // Past the mail guard, which is checked first.
                        ["Email:Provider"] = "Brevo",
                        ["Email:ApiKey"] = "xkeysib-not-a-real-key",
                        ["Email:FromAddress"] = "no-reply@khadra.test",
                        ["Documents:Provider"] = provider,
                    }));
            });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        });

        Assert.Contains("Documents:Provider", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Identity documents must not cross a network in the clear. A loopback address crosses no
    /// network, which is what lets a local stand-in be tested without loosening the rule that
    /// matters — anything else, private networks included, must be https.
    /// </summary>
    [Theory]
    [InlineData("http://project.supabase.co")]
    [InlineData("http://10.0.0.5:9099")]
    [InlineData("http://storage.internal")]
    public void Plain_http_is_refused_for_anything_that_is_not_loopback(string url)
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Resolve(Supabase(("Documents:Supabase:Url", url))));

        Assert.Contains("must be https", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://localhost:9099")]
    [InlineData("http://127.0.0.1:9099")]
    public void Plain_http_to_loopback_is_allowed(string url) =>
        Assert.IsType<SupabaseDocumentStorage>(
            Resolve(Supabase(("Documents:Supabase:Url", url))).GetRequiredService<IDocumentStorage>());
}
