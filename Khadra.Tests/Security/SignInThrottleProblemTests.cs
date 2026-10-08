using System.Text.Json;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.WebAPI.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.Tests.Security;

/// <summary>
/// The per-account sign-in refusal on the wire (pre-launch item 51). It must be indistinguishable from the rate
/// limiter's own 429 in <c>Program.cs</c> — same status, code, title and Retry-After header — because every client
/// already handles that one, and because a refusal worded differently would say which layer, and so which kind of
/// counting, had refused.
/// </summary>
public sealed class SignInThrottleProblemTests
{
    private sealed class Probe : ApiControllerBase
    {
        public Probe() => ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        public ObjectResult Answer(Error error) => Failure(error);
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_refused_name_answers_the_limiters_429_with_the_wait_as_Retry_After_only()
    {
        var probe = new Probe();

        var result = probe.Answer(IdentityErrors.TooManySignInAttempts(TimeSpan.FromSeconds(899.2)));

        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        Assert.Equal("900", probe.Response.Headers.RetryAfter.ToString());

        var body = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, Web)).RootElement;
        Assert.Equal("rate_limited", body.GetProperty("code").GetString());
        Assert.Equal("Too many requests. Try again later.", body.GetProperty("title").GetString());
        Assert.Equal("https://httpstatuses.com/429", body.GetProperty("type").GetString());
        // The wait is the header's business; the body keeps the limiter's shape exactly.
        Assert.False(body.TryGetProperty(Error.RetryAfterSecondsExtension, out _));
    }

    [Fact]
    public void A_wait_under_a_second_is_still_a_whole_second()
    {
        var error = IdentityErrors.TooManySignInAttempts(TimeSpan.FromMilliseconds(10));

        Assert.Equal(1, error.Extensions![Error.RetryAfterSecondsExtension]);
    }
}
