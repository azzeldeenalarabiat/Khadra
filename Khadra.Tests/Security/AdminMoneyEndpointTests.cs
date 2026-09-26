using System.Net;
using System.Reflection;
using Khadra.WebAPI;
using Khadra.WebAPI.Controllers;
using Khadra.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Khadra.Tests.Security;

/// <summary>
/// The administrator's money screens (payments Phase 4b) sit behind the administrator policy, every one
/// of them: the payments list, one payment's page, the refunds queue and the dashboard's finance panel.
/// A mistake here is a missing attribute, so the attributes are read, and an anonymous caller must
/// learn nothing.
/// </summary>
public sealed class AdminMoneyEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<WebApiAssemblyMarker> _factory =
        new WebApplicationFactory<WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.IsolateFromDeveloperDatabase();
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=khadra_tests;Username=x;Password=y");
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Database:AutoMigrate", "false");
                builder.UseSetting("Email:Provider", "Logging");
                builder.UseSetting("KnownProxies:0", "10.255.255.1");
            });

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("/api/v1/admin/payments")]
    [InlineData("/api/v1/admin/payments/vocabulary")]
    [InlineData("/api/v1/admin/payments/01a0d99c-8f0d-7092-8ec4-4176ed842a69")]
    [InlineData("/api/v1/admin/refunds")]
    [InlineData("/api/v1/admin/dashboard/finance")]
    public async Task Reading_the_platforms_money_without_a_token_is_401(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(typeof(AdminPaymentsController))]
    [InlineData(typeof(AdminDashboardController))]
    public void Every_action_sits_behind_the_administrator_policy(Type controller)
    {
        Assert.Equal(SecurityPolicies.Admin, controller.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
