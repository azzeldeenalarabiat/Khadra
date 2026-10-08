using Khadra.Bff.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// The console's idle timeout on the BFF's own clock (pre-launch item 129): thirty minutes without a PERSON using the
/// session ends it, however often the console polls in the background.
/// </summary>
public sealed class BffSessionActivityTests
{
    // The cache's own expiry runs on the real clock; these tests move "now" by hand and lean on the explicit
    // comparison in TouchAsync, which is what decides even when Redis has not yet dropped the key.
    private static readonly DateTimeOffset SignedIn = DateTimeOffset.UtcNow;

    private readonly MemoryDistributedCache _cache = new(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly SessionActivity _activity;

    public BffSessionActivityTests() =>
        _activity = new SessionActivity(_cache, Options.Create(new BffSecuritySettings { SessionIdleMinutes = 30 }));

    private async Task<AuthenticationProperties> SignedInSessionAsync()
    {
        var properties = new AuthenticationProperties();
        await _activity.StartAsync(properties, SignedIn);
        return properties;
    }

    private static HttpRequest Request(string? idleSeconds = null)
    {
        var context = new DefaultHttpContext();
        if (idleSeconds is not null)
            context.Request.Headers[SessionActivity.IdleHeaderName] = idleSeconds;
        return context.Request;
    }

    private Task<bool> AtAsync(AuthenticationProperties session, double minutes, string? idleSeconds = null) =>
        _activity.TouchAsync(session, Request(idleSeconds), SignedIn.AddMinutes(minutes));

    [Fact]
    public async Task Polling_from_an_unattended_console_does_not_keep_the_session_alive()
    {
        var session = await SignedInSessionAsync();

        // A poll every five minutes, each reporting how long the page has been untouched.
        for (var minute = 5; minute < 30; minute += 5)
            Assert.True(await AtAsync(session, minute, idleSeconds: (minute * 60).ToString(System.Globalization.CultureInfo.InvariantCulture)));

        Assert.False(await AtAsync(session, 30, idleSeconds: "1800"));
        // Over is over: a click a moment later does not bring it back.
        Assert.False(await AtAsync(session, 31));
    }

    [Fact]
    public async Task Somebody_using_the_console_keeps_it_alive_past_the_timeout()
    {
        var session = await SignedInSessionAsync();

        Assert.True(await AtAsync(session, 20, idleSeconds: "10"));
        Assert.True(await AtAsync(session, 45, idleSeconds: "0"));
        Assert.True(await AtAsync(session, 70));
        Assert.False(await AtAsync(session, 101, idleSeconds: "900"));
    }

    /// <summary>The website and an older console send no report: every request counts, exactly as before.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-60")]
    [InlineData("12.5")]
    [InlineData("soon")]
    public async Task A_request_without_a_readable_report_counts_as_activity_now(string? idleSeconds)
    {
        var session = await SignedInSessionAsync();

        Assert.True(await AtAsync(session, 25, idleSeconds));
        Assert.True(await AtAsync(session, 50));
    }

    [Fact]
    public async Task A_report_longer_than_the_timeout_is_held_to_it()
    {
        var session = await SignedInSessionAsync();

        Assert.Equal(SignedIn.AddMinutes(10), _activity.ActiveAt(Request("999999999"), SignedIn.AddMinutes(40)));
        Assert.True(await AtAsync(session, 5, idleSeconds: "999999999"));
    }

    [Fact]
    public async Task A_session_whose_activity_key_is_gone_is_over()
    {
        var session = await SignedInSessionAsync();
        await _cache.RemoveAsync($"bff:activity:{session.Items[SessionActivity.IdItem]}");

        Assert.False(await AtAsync(session, 1));
    }

    /// <summary>A ticket from before the change keeps the rule it was issued under, bounded by its absolute ceiling.</summary>
    [Fact]
    public async Task A_ticket_issued_before_the_change_is_left_to_its_own_expiry()
    {
        Assert.True(await _activity.TouchAsync(new AuthenticationProperties(), Request(), SignedIn.AddHours(3)));
    }

    [Fact]
    public async Task Each_session_has_its_own_key()
    {
        var first = await SignedInSessionAsync();
        var second = await SignedInSessionAsync();

        Assert.NotEqual(first.Items[SessionActivity.IdItem], second.Items[SessionActivity.IdItem]);
        Assert.True(await AtAsync(first, 29));
        Assert.False(await AtAsync(second, 30, idleSeconds: "1800"));
        Assert.True(await AtAsync(first, 50));
    }

    [Fact]
    public void The_idle_report_never_reaches_the_api()
    {
        Assert.Contains(SessionActivity.IdleHeaderName, ProxyRequestHeaders.NeverForwarded);
    }
}
