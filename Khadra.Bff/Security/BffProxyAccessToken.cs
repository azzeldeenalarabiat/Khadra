using System.Net.Http.Headers;
using Yarp.ReverseProxy.Model;

namespace Khadra.Bff.Security;

/// <summary>
/// Puts the server-held API token on a proxied call, in two halves: <see cref="AcquireAsync"/> gets it in the proxy
/// pipeline, and <see cref="AttachTo"/> sets it on the outgoing request in the request transform.
/// </summary>
/// <remarks>
/// <para>
/// Getting it may refresh, and a refresh can fail. It used to happen inside the request transform, where YARP catches
/// every exception and answers a bodiless 502 without the app's exception handler ever seeing it (pre-launch item 246;
/// YARP 2.3's <c>HttpForwarder</c> reports <c>RequestCreation</c>). So a refused session answered 502 rather than
/// 401, and a passing failure could not say "try again".
/// </para>
/// <para>
/// In the pipeline, before forwarding begins, the exception reaches <c>UseExceptionHandler</c> like the antiforgery
/// check beside it: 401 <c>bff.session_expired</c> for a refusal, 503 <c>bff.session_refresh_unavailable</c> with
/// <c>Retry-After</c> for anything else (<see cref="BffExceptionHandler"/>).
/// </para>
/// </remarks>
internal static class BffProxyAccessToken
{
    private const string ItemKey = "khadra:proxy_access_token";

    /// <summary>The proxy pipeline step: gets the session's access token, refreshing it if due, for a route that needs one.</summary>
    public static async Task AcquireAsync(HttpContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (RequiresSession(context))
        {
            var tokens = context.RequestServices.GetRequiredService<BffAccessTokenService>();
            context.Items[ItemKey] = await tokens.GetAccessTokenAsync(context, context.RequestAborted);
        }

        await next();
    }

    /// <summary>The request transform's half: the token <see cref="AcquireAsync"/> got, as the bearer.</summary>
    public static void AttachTo(HttpContext context, HttpRequestMessage proxyRequest)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(proxyRequest);

        // Never a silent call without credentials: the API would answer 401, and the response transform would sign a
        // perfectly good session out over a step that did not run.
        if (context.Items[ItemKey] is not string token)
            throw new InvalidOperationException("A route that needs the session reached the transform without a token: the pipeline step did not run.");

        proxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>Every proxied route needs the session unless its config says <c>"AuthorizationPolicy": "Anonymous"</c>.</summary>
    internal static bool RequiresSession(HttpContext context) =>
        !string.Equals(context.GetRouteModel().Config.AuthorizationPolicy, "Anonymous", StringComparison.OrdinalIgnoreCase);
}
