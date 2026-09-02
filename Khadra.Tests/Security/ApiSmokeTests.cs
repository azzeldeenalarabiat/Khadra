using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Khadra.Tests.Security;

// Boots the real WebAPI pipeline (no database calls are made by these requests) to prove the
// cross-cutting behaviour: default-deny auth, ProblemDetails shape, rate limiting, health probe.
public sealed class ApiSmokeTests : IDisposable
{
    private readonly WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> _factory = new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=khadra_tests;Username=x;Password=y");
            builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
            builder.UseSetting("Database:AutoMigrate", "false");
            builder.UseSetting("Email:Provider", "Logging");
        });

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Protected_endpoint_without_a_token_is_401()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_json_is_a_problem_details_with_trace_id()
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/api/v1/auth/login", UriKind.Relative), content);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.TryGetProperty("errors", out _));
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Missing_fields_fail_model_validation_with_field_errors()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/verify-email", UriKind.Relative), new { });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("Token", out _) || errors.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task Auth_endpoints_are_rate_limited_per_client()
    {
        using var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 11; attempt++)
        {
            using var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/verify-email", UriKind.Relative), new { });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(10, statuses.Count(status => status == HttpStatusCode.BadRequest));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task Liveness_probe_is_public()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
