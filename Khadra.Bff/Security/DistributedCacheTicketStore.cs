using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Khadra.Bff.Security;

// The browser cookie is only a random reference; the real ticket (claims + API tokens) lives encrypted
// in Redis. Losing Redis signs everyone out but never leaks tokens.
internal sealed class DistributedCacheTicketStore(
    IDistributedCache cache,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<BffSecuritySettings> settings) : ITicketStore
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("Khadra.Bff.SessionTicket.v1");
    private readonly TimeSpan _idleTimeout = TimeSpan.FromMinutes(settings.Value.SessionIdleMinutes);

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = $"bff:session:{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var protectedTicket = _protector.Protect(TicketSerializer.Default.Serialize(ticket));
        return cache.SetAsync(key, protectedTicket, new DistributedCacheEntryOptions
        {
            SlidingExpiration = _idleTimeout,
            AbsoluteExpiration = ticket.Properties.ExpiresUtc
        });
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var protectedTicket = await cache.GetAsync(key);
        if (protectedTicket is null)
            return null;

        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(protectedTicket));
        }
        catch (CryptographicException)
        {
            await cache.RemoveAsync(key);
            return null;
        }
    }

    public Task RemoveAsync(string key) => cache.RemoveAsync(key);
}

internal sealed class ConfigureSessionCookie(DistributedCacheTicketStore ticketStore)
    : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (name == BffConstants.CookieScheme)
            options.SessionStore = ticketStore;
    }
}
