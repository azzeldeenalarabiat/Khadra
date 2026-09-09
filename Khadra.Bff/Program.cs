using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Khadra.Bff;
using Khadra.Bff.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);
var environmentName = builder.Environment.EnvironmentName;

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
if (builder.Environment.IsDevelopment())
    builder.Logging.AddDebug();

builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddUserSecrets<BffAssemblyMarker>(optional: true);
builder.Configuration.AddEnvironmentVariables();

var securitySection = builder.Configuration.GetRequiredSection(BffSecuritySettings.SectionName);
var security = securitySection.Get<BffSecuritySettings>()
    ?? throw new InvalidOperationException("BffSecurity configuration is required.");
if (!Uri.TryCreate(security.ApiBaseUrl, UriKind.Absolute, out var apiBaseUri))
    throw new InvalidOperationException("BffSecurity:ApiBaseUrl must be an absolute URL.");

builder.Services.AddOptions<BffSecuritySettings>()
    .Bind(securitySection)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<BffExceptionHandler>();

// Redis holds the encrypted session tickets and the Data Protection key ring, so every BFF replica can
// read every session. Redis being down means nobody can sign in: fail fast at startup.
//
// Normalised first, because a managed platform hands this over as a redis:// URL and
// StackExchange.Redis reads its own comma-separated form. Failing fast is right, but crash-looping
// the console on the first deploy because the platform spelled the address the way every other
// client library expects is not.
var redis = await ConnectionMultiplexer.ConnectAsync(
    RedisConnectionString.Normalise(security.RedisConnection));
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis);
    options.InstanceName = "khadra-bff:";
});
builder.Services.AddDataProtection()
    .SetApplicationName("Khadra.Bff")
    .PersistKeysToStackExchangeRedis(redis, "khadra-bff:dataprotection-keys");

builder.Services.AddSingleton<DistributedCacheTicketStore>();
builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, ConfigureSessionCookie>();
builder.Services.AddScoped<BffAccessTokenService>();
builder.Services.AddScoped<AuthApiClient>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient(AuthApiClient.HttpClientName, client =>
    {
        client.BaseAddress = apiBaseUri;
        client.Timeout = TimeSpan.FromSeconds(15);
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false };
        if (builder.Environment.IsDevelopment())
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None ||
                certificate is System.Security.Cryptography.X509Certificates.X509Certificate2 { Subject: "CN=localhost" }; // untrusted ASP.NET dev certificate only
        return handler;
    });

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = BffConstants.CookieScheme;
        options.DefaultChallengeScheme = BffConstants.CookieScheme;
    })
    .AddCookie(BffConstants.CookieScheme, options =>
    {
        options.Cookie.Name = BffConstants.SessionCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.Cookie.Domain = null;
        options.ExpireTimeSpan = TimeSpan.FromHours(security.SessionAbsoluteHours);
        options.SlidingExpiration = false;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = BffConstants.XsrfHeaderName;
    // Never read the token from a form body: multipart uploads are streamed to the API by YARP.
    options.SuppressReadingTokenFromFormBody = true;
    options.Cookie.Name = BffConstants.AntiforgeryCookieName;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transformBuilder =>
    {
        var isAnonymousRoute = string.Equals(
            transformBuilder.Route?.AuthorizationPolicy, "Anonymous", StringComparison.OrdinalIgnoreCase);

        transformBuilder.AddRequestTransform(async transform =>
        {
            // The browser never chooses the API identity: strip its credentials, attach the server-held one.
            transform.ProxyRequest.Headers.Remove("Cookie");
            transform.ProxyRequest.Headers.Remove("Authorization");
            transform.ProxyRequest.Headers.Remove(BffConstants.XsrfHeaderName);

            var clientAddress = transform.HttpContext.Connection.RemoteIpAddress?.ToString();
            if (clientAddress is not null)
                transform.ProxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-For", clientAddress);

            if (isAnonymousRoute)
                return;

            var tokens = transform.HttpContext.RequestServices.GetRequiredService<BffAccessTokenService>();
            var accessToken = await tokens.GetAccessTokenAsync(transform.HttpContext, transform.HttpContext.RequestAborted);
            transform.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        });

        // The API says 401 on a proxied call when the bearer token it was given is no longer good:
        // the security stamp rotated because the password changed elsewhere, an admin suspended the
        // account, or a dealer owner deactivated this employee (spec 4.2: "their login stops working
        // immediately"). Until now the cookie session outlived that by up to the access token's
        // remaining life, so every call failed while /bff/user still said "signed in". End the
        // session the moment the API disowns it, so the next navigation lands on sign-in.
        transformBuilder.AddResponseTransform(async transform =>
        {
            if (isAnonymousRoute || transform.ProxyResponse?.StatusCode != System.Net.HttpStatusCode.Unauthorized)
                return;

            if (transform.HttpContext.User.Identity?.IsAuthenticated == true)
                await transform.HttpContext.SignOutAsync(BffConstants.CookieScheme);
        });
    });

// The BFF is the outermost hop: a browser connects to it directly, so the address on the connection
// IS the client and X-Forwarded-For arriving here was written by that client. Honouring it would let
// the browser rename itself â€” and because AddClientAddress passes RemoteIpAddress on to the API as
// the API's own X-Forwarded-For, a value invented here is laundered into the value the API trusts,
// putting the caller back in charge of its rate-limit partition one hop further along.
//
// So nothing is trusted unless it is named. The lists stay cleared (the framework default trusts
// loopback, which is not a decision to inherit silently) and are filled only from configuration.
// Unlike the API this does NOT refuse to start when empty: the BFF terminating TLS itself with no
// edge in front is a perfectly ordinary deployment, and empty is the correct, safe answer for it.
// Put a TLS-terminating edge in front and that edge belongs in this list and in the API's.
var trustedProxies = builder.Configuration.GetSection("KnownProxies").Get<string[]>() ?? [];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // How many proxies stand in front, counted from the RIGHT. One is correct behind a single edge
    // this deployment owns; a managed platform usually has more, and Render fronts a service with
    // Cloudflare AND its own load balancer. Configurable for the same reason the API's is, and it
    // matters more here: get it wrong and X-Forwarded-Proto may resolve to something that is not
    // https, which is enough to stop the __Host- antiforgery cookie being issued at all.
    options.ForwardLimit = builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var entry in trustedProxies)
    {
        // A container's address changes when it is recreated, so a range is often the only stable
        // way to name one. Accept both, and name the offending entry rather than failing obscurely.
        if (entry.Contains('/', StringComparison.Ordinal))
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(entry));
        else if (IPAddress.TryParse(entry, out var address))
            options.KnownProxies.Add(address);
        else
            throw new InvalidOperationException($"KnownProxies entry '{entry}' is not an IP address or CIDR range.");
    }
});
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});
builder.Services.AddHealthChecks().AddRedis(redis, name: "redis", tags: ["ready"]);

var app = builder.Build();

// A TLS-terminating edge in front and nothing trusted is not a survivable combination HERE, and it
// fails in a way nobody would diagnose from the symptom.
//
// The session and antiforgery cookies are __Host- prefixed with SecurePolicy.Always, which the
// antiforgery system enforces by REFUSING to issue a token when Request.IsHttps is false. Behind an
// edge that terminates TLS the request arrives as plain HTTP, so IsHttps is false unless
// X-Forwarded-Proto is honoured -- and it is honoured only when the sender is trusted. With nothing
// trusted, GET /bff/antiforgery answers 500, which is the FIRST call the sign-in page makes. The
// console loads perfectly and nobody can sign in, with an exception that talks about SSL
// configuration rather than about a proxy.
//
// So: in Production, say so at startup instead. Development is left alone, where the console is
// reached over http://localhost and there is genuinely no proxy to name.
if (trustedProxies.Length == 0 && !app.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "KnownProxies is empty. This BFF is behind something that terminates TLS -- otherwise it " +
        "would not be reachable over https -- and without naming it, X-Forwarded-Proto is ignored, " +
        "Request.IsHttps stays false, and the __Host- antiforgery cookie cannot be issued: every " +
        "sign-in fails with a 500 while the console itself loads. Set the \"KnownProxies\" " +
        "configuration array to the address or CIDR range the edge connects from.");
}

// What the first request looked like on the wire, and what the pipeline made of it.
//
// Registered BEFORE UseForwardedHeaders on purpose, and it reports from both sides of it: the
// values are read on the way in, then the rest of the pipeline runs, then the same HttpContext is
// read again on the way out. That ordering is not incidental -- ForwardedHeadersMiddleware CONSUMES
// the entry it uses, removing it from X-Forwarded-For and overwriting RemoteIpAddress, so a
// diagnostic placed after it can no longer say what actually arrived.
//
// It exists because the failure it diagnoses is unreadable from the symptom. If the peer is not
// inside a trusted range, X-Forwarded-Proto is ignored, Request.IsHttps stays false, and the
// __Host- antiforgery cookie is refused -- so /bff/antiforgery answers 500 with a message about SSL
// configuration, on a console that otherwise loads perfectly.
//
// Once, not per request: this is a fact about the deployment, not about traffic.
// Report the forwarding facts for requests that can actually tell us something.
//
// The previous version logged the FIRST request and spent its one shot on a platform health probe:
// a loopback connection from ::1 carrying no forwarding headers at all, arriving a minute before the
// browser request that failed. That line said nothing about the real hop, and acting on it would have
// meant trusting ::1 on the strength of a health check.
//
// So the trigger is the request that matters: /bff/antiforgery, the call that fails, plus the first
// few arriving from anywhere other than loopback. Capped, because this is a diagnostic and not an
// access log.
//
// EVERY forwarding header is reported verbatim rather than the two that were guessed at. Render fronts
// services with Cloudflare, and Cloudflare states the original scheme in CF-Visitor as well as in
// X-Forwarded-Proto; a platform sending one and not the other would otherwise be indistinguishable
// from a platform sending neither.
var forwardingLinesLogged = 0;
app.Use(async (context, next) =>
{
    var peerAddress = context.Connection.RemoteIpAddress;
    var fromLoopback = peerAddress is not null && IPAddress.IsLoopback(peerAddress);
    var isAntiforgery = context.Request.Path.StartsWithSegments("/bff/antiforgery");

    // A loopback request to anything else is a platform health probe: nothing to learn from it, and
    // it is what consumed the previous diagnostic before the real request ever arrived.
    if ((!isAntiforgery && fromLoopback) || Interlocked.Increment(ref forwardingLinesLogged) > 8)
    {
        await next(context).ConfigureAwait(false);
        return;
    }

    // Read on the way IN. ForwardedHeadersMiddleware consumes the entry it uses -- removing it from
    // X-Forwarded-For and overwriting RemoteIpAddress -- so none of this survives to the way out.
    var transportPeer = peerAddress?.ToString() ?? "(none)";
    var forwardingHeaders = string.Join(" | ", context.Request.Headers
        .Where(header =>
            header.Key.StartsWith("X-Forwarded", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase) ||
            header.Key.StartsWith("CF-", StringComparison.OrdinalIgnoreCase))
        .Select(header => header.Key + ": " + header.Value.ToString()));

    await next(context).ConfigureAwait(false);

    var headersText = forwardingHeaders.Length > 0
        ? forwardingHeaders
        : "(no forwarding headers of any kind)";
    var path = context.Request.Path.Value ?? "/";
    var resolvedClient = context.Connection.RemoteIpAddress?.ToString() ?? "(none)";
    var scheme = context.Request.Scheme;

    if (context.Request.IsHttps)
    {
        BffDiagnostics.LogForwardingHealthy(
            app.Logger, path, transportPeer, headersText, resolvedClient, scheme);
    }
    else
    {
        BffDiagnostics.LogForwardingNotHttps(
            app.Logger, path, transportPeer, headersText, resolvedClient, scheme);
    }
});

// Only when there is something to trust. With nothing named, RemoteIpAddress stays the address the
// request actually came from, which is exactly right for the outermost hop.
//
// This runs BEFORE HSTS, HTTPS redirection, authentication, authorization, antiforgery and the
// proxy -- every one of which reads the scheme or the client address that this establishes.
if (trustedProxies.Length > 0)
{
    app.UseForwardedHeaders();
    var trustedList = string.Join(", ", trustedProxies);
    var hops = (builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1)
        .ToString(System.Globalization.CultureInfo.InvariantCulture);
    BffDiagnostics.LogTrustedProxies(app.Logger, trustedList, hops);
}
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    var supplied = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(supplied) && supplied.Length <= 100 && supplied.All(character => character is >= '!' and <= '~'))
        context.TraceIdentifier = supplied;

    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self), payment=()";
        // Tighten to nonce-based styles + Trusted Types once the dashboard's PrimeNG usage is known.
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; " +
            "font-src 'self' data:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        context.Response.Headers.Remove("Server");
        return Task.CompletedTask;
    });

    await next();
});

app.UseHttpsRedirection();
// Static files, with the two caching rules a hashed SPA needs and does not get by default.
//
// Out of the box these responses carry an ETag and no Cache-Control, so a browser applies its own
// heuristic and may reuse index.html without asking. index.html is the one file that must never be
// reused: it names the content-hashed bundles, so a stale copy points at assets a deploy has already
// replaced -- a console that loads unstyled, or not at all, for anyone who visited before. It bit
// this deployment twice during testing, both times looking like a bug in the application.
//
// Everything else IS content-hashed, which means its name changes whenever its bytes do, so it can
// be cached hard and immutably. The pairing is the point: revalidate the index, never revalidate the
// assets it names.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var headers = context.Context.Response.GetTypedHeaders();
        var path = context.File.Name;

        if (path.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            headers.CacheControl = new Microsoft.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, MustRevalidate = true };
        }
        else
        {
            headers.CacheControl = new Microsoft.Net.Http.Headers.CacheControlHeaderValue
            {
                Public = true,
                MaxAge = TimeSpan.FromDays(365),
                Extensions = { new Microsoft.Net.Http.Headers.NameValueHeaderValue("immutable") },
            };
        }
    },
});
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/bff/antiforgery", (HttpContext context) =>
{
    var requestToken = IssueAntiforgeryCookie(context);
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new { requestToken });
}).AllowAnonymous();

app.MapPost("/bff/login", async (HttpContext context, IAntiforgery antiforgery, AuthApiClient api) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var request = await context.Request.ReadFromJsonAsync<BffLoginRequest>(context.RequestAborted);
    if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Problem(StatusCodes.Status400BadRequest, "Email and password are required.", "bff.invalid_request", null);

    var result = await api.LoginAsync(request.Email, request.Password, context.RequestAborted);
    if (result.Tokens is null)
        return ProblemFromApi(result);

    await SignInAsync(context, result.Tokens, security);
    return Results.Ok(ToSessionUser(result.Tokens.User));
}).AllowAnonymous();

app.MapPost("/bff/change-password", async (HttpContext context, IAntiforgery antiforgery, AuthApiClient api, BffAccessTokenService tokens) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var request = await context.Request.ReadFromJsonAsync<BffChangePasswordRequest>(context.RequestAborted);
    if (request is null)
        return Problem(StatusCodes.Status400BadRequest, "Current and new password are required.", "bff.invalid_request", null);

    var accessToken = await tokens.GetAccessTokenAsync(context, context.RequestAborted);
    var result = await api.ChangePasswordAsync(accessToken, request.CurrentPassword, request.NewPassword, context.RequestAborted);
    if (result.Tokens is null)
        return ProblemFromApi(result);

    await SignInAsync(context, result.Tokens, security);
    return Results.Ok(ToSessionUser(result.Tokens.User));
}).RequireAuthorization();

app.MapPost("/bff/logout", async (HttpContext context, IAntiforgery antiforgery, AuthApiClient api) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var authentication = await context.AuthenticateAsync(BffConstants.CookieScheme);
    var accessToken = authentication.Properties?.GetTokenValue(BffConstants.AccessTokenName);
    var refreshToken = authentication.Properties?.GetTokenValue(BffConstants.RefreshTokenName);
    if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken))
        await api.LogoutAsync(accessToken, refreshToken, context.RequestAborted);

    await context.SignOutAsync(BffConstants.CookieScheme);
    context.User = new ClaimsPrincipal(new ClaimsIdentity());
    IssueAntiforgeryCookie(context);
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/bff/user", (HttpContext context) => Results.Ok(new
{
    isAuthenticated = true,
    id = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
    email = context.User.FindFirstValue(BffConstants.EmailClaim),
    fullName = context.User.Identity?.Name,
    phone = context.User.FindFirstValue(BffConstants.PhoneClaim),
    role = context.User.FindFirstValue(ClaimTypes.Role),
    isEmailVerified = context.User.HasClaim(BffConstants.EmailVerifiedClaim, "true"),
    mustChangePassword = context.User.HasClaim(BffConstants.MustChangePasswordClaim, "true")
})).RequireAuthorization();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

// Default-deny for the proxied API: every route needs a session unless its config says
// "AuthorizationPolicy": "Anonymous" (register, verify-email, resend-verification, forgot/reset password).
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.Use(async (context, next) =>
    {
        if (!HttpMethods.IsGet(context.Request.Method) &&
            !HttpMethods.IsHead(context.Request.Method) &&
            !HttpMethods.IsOptions(context.Request.Method))
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
        }

        await next();
    });
}).RequireAuthorization();

// Serves the built Angular dashboard from wwwroot in production; in development ng serve proxies here.
// The SPA fallback serves index.html for every client-side route, and it does NOT go through the
// static-file options above -- so the no-cache rule is repeated here or a deep link would still be
// served from a stale copy.
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context =>
        context.Context.Response.GetTypedHeaders().CacheControl =
            new Microsoft.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, MustRevalidate = true },
}).AllowAnonymous();

await app.RunAsync();

static async Task SignInAsync(HttpContext context, ApiAuthTokens tokens, BffSecuritySettings security)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, tokens.User.Id.ToString()),
        new(ClaimTypes.Name, tokens.User.FullName),
        new(ClaimTypes.Role, tokens.User.Role),
        new(BffConstants.EmailClaim, tokens.User.Email),
        new(BffConstants.PhoneClaim, tokens.User.Phone),
        new(BffConstants.EmailVerifiedClaim, tokens.User.IsEmailVerified ? "true" : "false"),
        new(BffConstants.MustChangePasswordClaim, tokens.User.MustChangePassword ? "true" : "false")
    };

    var now = DateTimeOffset.UtcNow;
    var absoluteExpiry = now.AddHours(security.SessionAbsoluteHours);
    var properties = new AuthenticationProperties
    {
        AllowRefresh = false,
        IsPersistent = true,
        IssuedUtc = now,
        ExpiresUtc = tokens.RefreshTokenExpiresAt < absoluteExpiry ? tokens.RefreshTokenExpiresAt : absoluteExpiry
    };
    BffAccessTokenService.StoreTokens(properties, tokens);

    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, BffConstants.CookieScheme, ClaimTypes.Name, ClaimTypes.Role));
    await context.SignInAsync(BffConstants.CookieScheme, principal, properties);

    // Antiforgery tokens are bound to the identity; re-issue one for the now-authenticated principal.
    context.User = principal;
    IssueAntiforgeryCookie(context);
}

static string IssueAntiforgeryCookie(HttpContext context)
{
    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Cookies.Append(BffConstants.XsrfCookieName, tokens.RequestToken!, new CookieOptions
    {
        HttpOnly = false,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true
    });
    return tokens.RequestToken!;
}

static IResult ProblemFromApi(AuthApiResult result)
{
    var status = (int)result.StatusCode;
    var title = result.Problem?.Title ?? "The request was rejected.";
    var code = result.Problem?.Code ?? "api.rejected";
    return Problem(status, title, code, result.Problem?.Errors);
}

static IResult Problem(int status, string title, string code, Dictionary<string, string[]>? errors)
{
    var extensions = new Dictionary<string, object?> { ["code"] = code };
    if (errors is not null)
        extensions["errors"] = errors;
    return Results.Problem(statusCode: status, title: title, type: $"https://httpstatuses.com/{status}", extensions: extensions);
}

static object ToSessionUser(ApiUser user) => new
{
    isAuthenticated = true,
    id = user.Id,
    email = user.Email,
    fullName = user.FullName,
    phone = user.Phone,
    role = user.Role,
    isEmailVerified = user.IsEmailVerified,
    mustChangePassword = user.MustChangePassword
};

internal sealed record BffLoginRequest(string Email, string Password);

internal sealed record BffChangePasswordRequest(string CurrentPassword, string NewPassword);


/// <summary>Startup and first-request diagnostics for the BFF.</summary>
/// <remarks>
/// A named class rather than log calls inline: CA1848 requires the source-generated path, and a
/// top-level-statements Program has nowhere to put a partial method.
/// </remarks>
internal static partial class BffDiagnostics
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Forwarded headers trusted from: {Proxies}, reading {ForwardLimit} hop(s) from " +
                  "the right.")]
    internal static partial void LogTrustedProxies(ILogger logger, string proxies, string forwardLimit);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Forwarding honoured on {Path}. Transport peer {TransportPeer}. Headers: " +
                  "{ForwardingHeaders}. Resolved to client {ResolvedClient} over {Scheme}, " +
                  "IsHttps True.")]
    internal static partial void LogForwardingHealthy(
        ILogger logger,
        string path,
        string transportPeer,
        string forwardingHeaders,
        string resolvedClient,
        string scheme);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "REQUEST NOT SEEN AS HTTPS on {Path}. Transport peer {TransportPeer}. Headers: " +
                  "{ForwardingHeaders}. Resolved to client {ResolvedClient} over {Scheme}. The " +
                  "__Host- antiforgery cookie cannot be issued over a non-SSL request, so sign-in " +
                  "fails here with a 500 while the console itself loads. If the headers above " +
                  "CONTAIN a proto, add a range covering {TransportPeer} to KnownProxies. If they " +
                  "contain NONE, the platform is not sending them and no KnownProxies value will " +
                  "help.")]
    internal static partial void LogForwardingNotHttps(
        ILogger logger,
        string path,
        string transportPeer,
        string forwardingHeaders,
        string resolvedClient,
        string scheme);
}
