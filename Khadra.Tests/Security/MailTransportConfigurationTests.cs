using System.Net;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure;
using Khadra.Infrastructure.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Khadra.Tests.Security;

/// <summary>
/// Which mail transport a given configuration selects, and which configurations are refused outright.
/// </summary>
/// <remarks>
/// The transport is chosen by matching a string from the environment, and the branch that caught
/// everything unrecognised used to be silent — it registered <c>LoggingEmailSender</c>, which accepts
/// every message, writes it to the log and delivers nothing. Nothing else in the system contradicted
/// that: sends "succeeded", the handler returned success, the API answered 202, and the startup check
/// printed "Email ready".
///
/// So a misspelt provider, or the documentation's own <c>Email__Provider="Brevo"</c> pasted into a
/// dashboard field with its quotes attached, produced a platform that looked healthy and sent nothing
/// — for days, in production, through registration, administrator invitation and password reset.
///
/// Two rules now, tested here: an unrecognised value is refused everywhere, and the Logging transport
/// is refused in Production specifically.
/// </remarks>
public sealed class MailTransportConfigurationTests
{
    private static ServiceProvider Resolve(string? provider)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=khadra_tests;Username=x;Password=y",
            ["Authentication:Jwt:SigningKey"] = new string('k', 48),
            ["Email:Provider"] = provider,
            ["Email:ApiKey"] = "xkeysib-not-a-real-key",
            ["Email:FromAddress"] = "no-reply@khadra.test",
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>Case and stray whitespace must not change which transport is chosen.</summary>
    [Theory]
    [InlineData("Brevo")]
    [InlineData("brevo")]
    [InlineData("  Brevo  ")]
    public void A_recognised_provider_selects_its_transport_whatever_the_case_or_spacing(string provider) =>
        Assert.IsType<BrevoEmailSender>(Resolve(provider).GetRequiredService<IEmailSender>());

    /// <summary>Asked for explicitly, or left unset, the logging transport is still available.</summary>
    [Theory]
    [InlineData("Logging")]
    [InlineData(null)]
    [InlineData("")]
    public void The_logging_transport_is_selected_only_when_it_was_asked_for_or_nothing_was(string? provider) =>
        Assert.IsType<LoggingEmailSender>(Resolve(provider).GetRequiredService<IEmailSender>());

    /// <summary>
    /// The regression. Each of these used to select the transport that delivers nothing, silently.
    /// The quoted one is the likeliest of all: it is what the deployment guide's own bash example
    /// produces when pasted whole into a dashboard field that needs a bare value.
    /// </summary>
    [Theory]
    [InlineData("\"Brevo\"")]
    [InlineData("'Brevo'")]
    [InlineData("Brevoo")]
    [InlineData("Brevo,")]
    [InlineData("Bravo")]
    public void A_provider_that_matches_nothing_is_refused_and_names_itself(string provider)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Resolve(provider));

        Assert.Contains("Email:Provider", failure.Message, StringComparison.Ordinal);
        // The offending value, so a stray quote or space is visible rather than inferred.
        Assert.Contains(provider, failure.Message, StringComparison.Ordinal);
        Assert.Contains("Brevo", failure.Message, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> Api(
        string environment,
        string? provider) =>
        new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=khadra_tests;Username=x;Password=y");
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Database:AutoMigrate", "false");
                builder.UseSetting("KnownProxies:0", "10.255.255.1");

                // NOT UseSetting for this one. Program.cs re-adds appsettings.Local.json — untracked,
                // developer-only — after the host's own settings, so it outranks UseSetting. A
                // developer whose local file names a real transport would have seen this test pass
                // for the wrong reason, and one without the file would have seen it pass for the
                // right one. An appended source is the highest precedence there is, so the test
                // states the configuration rather than negotiating for it.
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Email:Provider"] = provider }));
            });

    /// <summary>
    /// Production is refused on the transport that delivers nothing — including when the setting is
    /// simply absent, which is how it is selected in practice, because it is the default.
    /// </summary>
    [Theory]
    [InlineData("Logging")]
    [InlineData(null)]
    public async Task Production_refuses_to_start_on_the_transport_that_delivers_nothing(string? provider)
    {
        using var factory = Api("Production", provider);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        });

        Assert.Contains("Email:Provider", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a test host still boots on it. This is why the guard asks IsProduction rather than
    /// !IsDevelopment, as the KnownProxies guard beside it does: a test host has no mail server and
    /// legitimately never will, so borrowing that shape would have taken the whole suite down.
    /// </summary>
    [Theory]
    [InlineData("Testing")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public async Task Every_other_environment_still_starts_on_it(string environment)
    {
        using var factory = Api(environment, "Logging");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
