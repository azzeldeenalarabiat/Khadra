using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
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
/// Two rotations of one refresh token that race: what the LOSER's client is sent (pre-launch item 240).
/// </summary>
/// <remarks>
/// <para>
/// The race is real on PostgreSQL: both requests load the token unrevoked, the first commits its rotation, and the
/// second's save fails on <c>xmin</c>, which <c>UnitOfWork</c> turns into <see cref="ConcurrencyConflictException"/>.
/// The unit of work below raises exactly that, so the loser's path runs end to end through the real controller,
/// handler and error mapping.
/// </para>
/// <para>
/// It used to answer 401 <c>auth.invalid_refresh_token</c>. Every client reads a 401 from this endpoint as a verdict: the
/// app ends the session and never presents again, the BFF signs out. When the client was waiting on the loser — the
/// app's second presentation after a timeout, while the first was still in the handler — a live family was thrown
/// away. Now 503 <c>auth.refresh_conflict</c>, which every app build from 1.1.0 treats as a network event: it keeps the
/// token it holds.
/// </para>
/// </remarks>
public sealed class RefreshRaceTests : IDisposable
{
    private readonly User _customer = Users.Customer();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> _factory;

    public RefreshRaceTests()
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_customer);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new ConcurrencyConflictException("xmin changed"));

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
                // No database: the token as the handler loads it, the account behind it, and a unit of work whose save
                // loses the race.
                builder.ConfigureTestServices(services =>
                {
                    services.AddScoped(_ => users);
                    services.AddScoped(_ => _refreshTokens);
                    services.AddScoped(_ => _unitOfWork);
                });
            });
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>A live token of a fresh family, stored under the hash the API itself computes.</summary>
    private (string Raw, RefreshToken Stored) LiveToken()
    {
        using var scope = _factory.Services.CreateScope();
        var generated = scope.ServiceProvider.GetRequiredService<IOpaqueTokenService>().Generate();
        var token = RefreshToken.IssueNewFamily(
            _customer.Id, generated.Hash, DateTimeOffset.UtcNow, TimeSpan.FromDays(14), TimeSpan.FromDays(30), "127.0.0.1", "xunit");
        _refreshTokens.GetByHashAsync(generated.Hash, Arg.Any<CancellationToken>()).Returns(token);
        return (generated.Value, token);
    }

    [Fact]
    public async Task The_loser_of_a_race_is_told_to_try_again_never_that_the_session_is_over()
    {
        var (raw, stored) = LiveToken();
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/refresh", UriKind.Relative), new { refreshToken = raw });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("auth.refresh_conflict", problem.RootElement.GetProperty("code").GetString());
        // The family survives: the winner's replacement is the live token, and nothing retires it.
        await _refreshTokens.DidNotReceive()
            .RevokeFamilyAsync(stored.FamilyId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
