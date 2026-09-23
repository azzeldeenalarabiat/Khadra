using Khadra.Bff.Security;
using Microsoft.AspNetCore.Authentication;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// What keeps two BFF deployments (the staff console and the customer website) apart, and how long a
/// session lives once there is more than one kind of user behind a BFF.
/// </summary>
public sealed class BffDeploymentTests
{
    [Fact]
    public void The_console_keeps_the_names_its_existing_sessions_and_key_ring_live_under()
    {
        // Changing any of these signs every staff member out on the next deploy and orphans the ring
        // their cookies were protected with.
        var realm = BffRealm.For("console");

        Assert.Equal("khadra-bff:", realm.CacheInstanceName);
        Assert.Equal("Khadra.Bff", realm.DataProtectionApplicationName);
        Assert.Equal("khadra-bff:dataprotection-keys", realm.KeyRingKey);
    }

    [Fact]
    public void Another_deployment_shares_none_of_the_consoles_names()
    {
        var console = BffRealm.For("console");
        var web = BffRealm.For("customer-web");

        Assert.NotEqual(console.CacheInstanceName, web.CacheInstanceName);
        Assert.NotEqual(console.DataProtectionApplicationName, web.DataProtectionApplicationName);
        Assert.NotEqual(console.KeyRingKey, web.KeyRingKey);
        // A prefix of the other would let one deployment's keys fall inside the other's namespace.
        Assert.False(web.CacheInstanceName.StartsWith(console.CacheInstanceName, StringComparison.Ordinal));
    }

    [Fact]
    public void Allowed_roles_are_read_as_a_trimmed_exact_set()
    {
        var settings = new BffSecuritySettings { ApiBaseUrl = "https://api.example/", AllowedRoles = " Admin, DealerOwner ,,DealerEmployee" };

        var roles = settings.AllowedRoleSet();

        Assert.Equal(3, roles.Count);
        Assert.Contains("DealerOwner", roles);
        Assert.DoesNotContain("Customer", roles);
        Assert.DoesNotContain("admin", roles); // exact: role names are the API's, not a guess at them
    }

    private static readonly DateTimeOffset SignIn = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_staff_session_ends_at_its_ceiling_even_though_the_refresh_token_lives_longer()
    {
        var properties = new AuthenticationProperties();
        SessionLifetime.Start(properties, SignIn, SignIn.AddDays(14), absoluteHours: 8);

        Assert.Equal(SignIn.AddHours(8), properties.ExpiresUtc);

        // A refresh during the shift changes nothing: the ceiling is counted from sign-in.
        SessionLifetime.Extend(properties, SignIn.AddHours(7).AddDays(14), absoluteHours: 8);
        Assert.Equal(SignIn.AddHours(8), properties.ExpiresUtc);
    }

    [Fact]
    public void A_customer_session_follows_each_refreshed_token_up_to_its_ceiling()
    {
        var properties = new AuthenticationProperties();
        SessionLifetime.Start(properties, SignIn, SignIn.AddDays(14), absoluteHours: 720);
        Assert.Equal(SignIn.AddDays(14), properties.ExpiresUtc);

        // Day 10: the token rotates and its own expiry moves; the session moves with it.
        SessionLifetime.Extend(properties, SignIn.AddDays(24), absoluteHours: 720);
        Assert.Equal(SignIn.AddDays(24), properties.ExpiresUtc);

        // Day 20: the new token would outlive the ceiling, and the ceiling wins.
        SessionLifetime.Extend(properties, SignIn.AddDays(34), absoluteHours: 720);
        Assert.Equal(SignIn.AddDays(30), properties.ExpiresUtc);
    }

    [Fact]
    public void A_ticket_from_before_the_sign_in_time_was_recorded_can_shrink_but_never_grow()
    {
        var properties = new AuthenticationProperties { ExpiresUtc = SignIn.AddHours(8) };

        SessionLifetime.Extend(properties, SignIn.AddDays(14), absoluteHours: 720);
        Assert.Equal(SignIn.AddHours(8), properties.ExpiresUtc);

        SessionLifetime.Extend(properties, SignIn.AddHours(2), absoluteHours: 720);
        Assert.Equal(SignIn.AddHours(2), properties.ExpiresUtc);
    }
}
