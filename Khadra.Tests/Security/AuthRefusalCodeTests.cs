using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Khadra.Application.Common.Ports;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Khadra.Tests.Security;

/// <summary>
/// Every refusal the API makes about WHO is asking carries a stable <c>code</c>, and every answer tells the browser
/// not to second-guess its type (E2E F10).
/// </summary>
/// <remarks>
/// A bare 401 and a role-based 403 used to arrive with no code: the console rendered a bodiless 403 as "the service
/// did not respond", which reads as a broken platform to someone who is simply not allowed. The status is unchanged
/// in every case — the customer app refreshes once on any 401, and both BFFs end their session on one — so these
/// pin the status as firmly as the code.
/// </remarks>
public sealed class AuthRefusalCodeTests : IDisposable
{
    private readonly User _customer = Users.Customer();
    private readonly WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> _factory;

    public AuthRefusalCodeTests()
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_customer);

        _factory = new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.IsolateFromDeveloperDatabase();
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=khadra_tests;Username=x;Password=y");
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Database:AutoMigrate", "false");
                builder.UseSetting("Email:Provider", "Logging");
                builder.UseSetting("KnownProxies:0", "10.255.255.1");
                // The token's account, as the security-stamp check reads it — no database is involved.
                builder.ConfigureTestServices(services => services.AddScoped(_ => users));
            });
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task No_token_is_a_401_that_says_so_with_a_code_and_the_usual_challenge()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ProblemOf(response);
        Assert.Equal("auth.unauthenticated", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task A_refused_token_is_a_401_that_names_the_session_not_the_reason()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        using var response = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        var problem = await ProblemOf(response);
        Assert.Equal("auth.session_invalid", problem.GetProperty("code").GetString());
        // What was wrong with the token stays on the server.
        Assert.DoesNotContain("IDX", problem.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_role_the_endpoint_does_not_admit_is_a_403_with_a_code()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(_customer));

        using var response = await client.GetAsync(new Uri("/api/v1/admin/settings/business-rules", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ProblemOf(response);
        Assert.Equal("auth.forbidden", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Every_answer_tells_the_browser_not_to_sniff_its_type()
    {
        using var client = _factory.CreateClient();

        using var config = await client.GetAsync(new Uri("/api/v1/app-config", UriKind.Relative));
        using var refused = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));
        using var missingImage = await client.GetAsync(new Uri($"/api/v1/vehicle-images/vehicles/{Guid.NewGuid()}/x.jpg", UriKind.Relative));

        foreach (var response in new[] { config, refused, missingImage })
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    private string TokenFor(User user)
    {
        using var scope = _factory.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>();
        return issuer.Issue(user, Guid.NewGuid(), DateTimeOffset.UtcNow).Token;
    }

    private static async Task<JsonElement> ProblemOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
