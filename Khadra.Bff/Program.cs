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
var redis = await ConnectionMultiplexer.ConnectAsync(security.RedisConnection);
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

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});
builder.Services.AddHealthChecks().AddRedis(redis, name: "redis", tags: ["ready"]);

var app = builder.Build();

app.UseForwardedHeaders();
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
app.UseStaticFiles();
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
app.MapFallbackToFile("index.html").AllowAnonymous();

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

