using System.Net;
using System.Reflection;
using Khadra.WebAPI;
using Khadra.WebAPI.Controllers;
using Khadra.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Khadra.Tests.Security;

/// <summary>
/// The transport side of a booking's financial state (payments Phase 4). Who reads which projection is
/// proven against the handler (<c>BookingFinancialsQueryTests</c>); what is left is the layer above it,
/// where a mistake is a missing attribute: an anonymous caller must learn nothing, and the
/// administrator's twin must sit behind the administrator policy.
/// </summary>
public sealed class BookingFinancialsEndpointTests : IDisposable
{
    private static readonly Guid BookingId = Guid.CreateVersion7();

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
    [InlineData("/api/v1/bookings/{0}/financials")]
    [InlineData("/api/v1/admin/bookings/{0}/financials")]
    public async Task Reading_a_bookings_financial_state_without_a_token_is_401(string template)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, template, BookingId), UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void The_administrators_twin_sits_behind_the_administrator_policy()
    {
        var policy = typeof(AdminBookingsController).GetCustomAttribute<AuthorizeAttribute>()?.Policy;
        var action = typeof(AdminBookingsController).GetMethod(nameof(AdminBookingsController.GetFinancials))!;

        Assert.Equal(SecurityPolicies.Admin, policy);
        // No action-level attribute may loosen it.
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("{bookingId:guid}/financials", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void The_partys_endpoint_needs_a_signed_in_caller_and_decides_the_party_in_the_handler()
    {
        var action = typeof(BookingsController).GetMethod(nameof(BookingsController.GetFinancials))!;

        Assert.NotNull(typeof(BookingsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        // No role policy on the action: customers and office staff both reach it, and the handler
        // answers "not found" to anyone who is not a party to the booking.
        Assert.Null(action.GetCustomAttribute<AuthorizeAttribute>());
    }
}
