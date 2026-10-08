using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Khadra.Tests.Support;
using Khadra.WebAPI;
using Khadra.WebAPI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Khadra.Tests.Security;

/// <summary>
/// The transport side of review moderation (pre-launch item 81). The rule is proven against the handlers in
/// <c>ReviewModerationTests</c>; here a mistake is a missing attribute, so the policy is pinned by reflection and the
/// unauthenticated answer over the wire, as in <c>AdminCustomerDocumentEndpointTests</c>.
/// </summary>
public sealed class AdminReviewsEndpointTests : IDisposable
{
    private static readonly Guid ReviewId = Guid.CreateVersion7();

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

    [Fact]
    public async Task Listing_hiding_or_restoring_without_a_token_is_401()
    {
        using var client = _factory.CreateClient();

        using var list = await client.GetAsync(new Uri("/api/v1/admin/reviews", UriKind.Relative));
        using var hide = await client.PostAsJsonAsync(
            new Uri($"/api/v1/admin/reviews/{ReviewId}/hide", UriKind.Relative), new { reason = "AbusiveLanguage" });
        using var restore = await client.PostAsync(new Uri($"/api/v1/admin/reviews/{ReviewId}/restore", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, hide.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, restore.StatusCode);
    }

    /// <summary>Administrators only, from the class, and no action loosens it.</summary>
    [Fact]
    public void Every_route_is_for_administrators_only()
    {
        var controller = typeof(AdminReviewsController);
        Assert.Equal(SecurityPolicies.Admin, controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Single().Policy);

        foreach (var name in new[] { nameof(AdminReviewsController.List), nameof(AdminReviewsController.Hide), nameof(AdminReviewsController.Restore) })
        {
            var action = controller.GetMethod(name)!;
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
            Assert.Empty(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        }
    }
}
