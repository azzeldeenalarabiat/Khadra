using System.Globalization;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Khadra.Application;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.AdminUsers;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Infrastructure;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Security;
using Khadra.WebAPI;
using Khadra.WebAPI.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var environmentName = builder.Environment.EnvironmentName;

builder.Logging.ClearProviders();
// Scopes included, so every line carries the RequestId that produced it. Without them the only thing
// tying a delivery failure (event 1100) or a logged message (1200) to the request that caused it is
// the timestamp, which stops being enough the moment two people ask for a password reset in the same
// second -- and those are exactly the lines somebody reads when a reset produced no email.
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
if (builder.Environment.IsDevelopment())
    builder.Logging.AddDebug();

// Precedence: appsettings.json < appsettings.{env}.json < appsettings.Local.json (gitignored)
// < user secrets (Development) < environment variables. Tracked files never hold secrets.
builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddUserSecrets<WebApiAssemblyMarker>(optional: true);
builder.Configuration.AddEnvironmentVariables();

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Instance = context.HttpContext.Request.Path;
    };
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddScoped<ICurrentActor, HttpCurrentActor>();
builder.Services.AddScoped<IAuthorizationHandler, ApprovedDealerAuthorizationHandler>();
// A denied authorization returns ProblemDetails with a stable code, not an empty 403.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Authentication:Jwt configuration is required.");
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey ?? string.Empty)),
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = KhadraClaimTypes.Name,
            RoleClaimType = KhadraClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            // A valid signature is not enough: the account must still exist, be active, and carry the
            // current security stamp (password change / suspension / deletion rotate it).
            OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirst(KhadraClaimTypes.Subject)?.Value;
                var stamp = context.Principal?.FindFirst(KhadraClaimTypes.SecurityStamp)?.Value;
                if (!Guid.TryParse(subject, out var userId) || !Guid.TryParse(stamp, out var securityStamp))
                {
                    context.Fail("Required identity claims are missing.");
                    return;
                }

                var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
                var user = await users.GetByIdAsync(userId, context.HttpContext.RequestAborted);
                if (user is null || user.Status != UserStatus.Active || !user.MatchesSecurityStamp(securityStamp))
                    context.Fail("The user session is no longer valid.");
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Default-deny: every endpoint requires a valid bearer token unless marked [AllowAnonymous].
    options.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy(SecurityPolicies.Admin, policy => policy.RequireRole(UserRole.Admin.Name));
    options.AddPolicy(SecurityPolicies.DealerStaff, policy =>
        policy.RequireRole(UserRole.DealerOwner.Name, UserRole.DealerEmployee.Name));
    options.AddPolicy(SecurityPolicies.DealerOwner, policy => policy.RequireRole(UserRole.DealerOwner.Name));
    options.AddPolicy(SecurityPolicies.Customer, policy => policy.RequireRole(UserRole.Customer.Name));
    options.AddPolicy(SecurityPolicies.VerifiedEmail, policy => policy.RequireClaim(KhadraClaimTypes.EmailVerified, "true"));
    // Being a dealer owner is not enough: the BUSINESS has to be approved before it may operate.
    options.AddPolicy(SecurityPolicies.ApprovedDealer, policy => policy
        .RequireRole(UserRole.DealerOwner.Name)
        .AddRequirements(new ApprovedDealerRequirement()));
    options.AddPolicy(SecurityPolicies.ApprovedDealerStaff, policy => policy
        .RequireRole(UserRole.DealerOwner.Name, UserRole.DealerEmployee.Name)
        .AddRequirements(new ApprovedDealerRequirement()));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        // Retry-After, so a client can back off honestly instead of guessing. A phone that guesses
        // wrong either hammers a limit it is already over or leaves a customer waiting far longer
        // than they need to.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests. Try again later.",
            Type = "https://httpstatuses.com/429",
            Extensions = { ["traceId"] = context.HttpContext.TraceIdentifier, ["code"] = "rate_limited" }
        }, cancellationToken);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientAddress(context), _ => FixedWindow(600, TimeSpan.FromMinutes(1), queueLimit: 50)));
    // The auth limits are keyed on the address AND the account being named, not the address alone.
    // A Jordanian carrier NATs thousands of subscribers behind one IPv4 address, so an
    // address-only bucket meant ten sign-in attempts shared by a whole network. Keying on the pair
    // keeps brute force on one account just as tight while letting strangers coexist.
    options.AddPolicy(RateLimitPolicies.Auth, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            CredentialSubject.PartitionKey(context, ClientAddress(context)),
            _ => FixedWindow(10, TimeSpan.FromMinutes(1))));
    options.AddPolicy(RateLimitPolicies.Login, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            CredentialSubject.PartitionKey(context, ClientAddress(context)),
            _ => FixedWindow(10, TimeSpan.FromMinutes(15))));
    // A refresh is per DEVICE, and a carrier address carries thousands of them.
    options.AddPolicy(RateLimitPolicies.Refresh, context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientAddress(context), _ => FixedWindow(600, TimeSpan.FromMinutes(1))));
    // Browsing is chatty and shared: a customer scrolling results and opening cars makes many reads,
    // and a whole mobile network arrives from one address. Read-only public prices, so the ceiling
    // is there to stop a scraper, not to ration customers.
    options.AddPolicy(RateLimitPolicies.Public, context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientAddress(context), _ => FixedWindow(1200, TimeSpan.FromMinutes(1), queueLimit: 20)));
    // A provider catching up after an outage delivers a burst, and each delivery is somebody's
    // money. Queued rather than rejected for the same reason: a 429 makes the provider retry later,
    // which is strictly worse than making it wait a moment now.
    options.AddPolicy(RateLimitPolicies.Webhook, context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientAddress(context), _ => FixedWindow(600, TimeSpan.FromMinutes(1), queueLimit: 50)));
});

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection($"{AppOptions.SectionName}:AllowedOrigins").Get<string[]>() ?? [];
    var allowLocalNetwork = builder.Environment.IsDevelopment();

    // ONE predicate covering both rules. `SetIsOriginAllowed` REPLACES the check
    // that `WithOrigins` installs rather than adding to it, so calling both leaves
    // only the second -- which silently locked the console out of its own API.
    policy
        .SetIsOriginAllowed(origin =>
            origins.Contains(origin, StringComparer.OrdinalIgnoreCase) ||
            (allowLocalNetwork && IsLocalNetworkAppOrigin(origin)))
        .AllowAnyHeader()
        .AllowAnyMethod();
}));

/// <summary>
/// The customer app served from this machine's own address on the local network,
/// so it can be opened on a real phone.
/// </summary>
/// <remarks>
/// A predicate rather than another entry in AllowedOrigins because the address is
/// a DHCP lease: it moved from .251 to .254 in the middle of one test run, and a
/// configured origin means editing and restarting every time that happens.
///
/// Deliberately narrow, and it is three conditions rather than one: the caller
/// checks this only in development, and it accepts only plaintext http on the one
/// port the app is served from, at a PRIVATE address. Nothing routable from the
/// internet matches. Note also that this API authenticates with a bearer token and
/// not a cookie, so a permitted origin cannot ride on a session the way it could
/// if `AllowCredentials` were in play.
/// </remarks>
static bool IsLocalNetworkAppOrigin(string origin)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
    if (uri.Scheme != Uri.UriSchemeHttp || uri.Port != 4300) return false;
    if (!System.Net.IPAddress.TryParse(uri.Host, out var address)) return false;
    if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;

    // RFC 1918, plus loopback. Anything else is not a machine on somebody's desk.
    var octets = address.GetAddressBytes();
    return octets[0] switch
    {
        10 => true,
        127 => true,
        172 => octets[1] >= 16 && octets[1] <= 31,
        192 => octets[1] == 168,
        _ => false,
    };
}

// This API is reached two ways, and an earlier version of this comment claimed only the first.
//
//   * through the BFF, which proxies the admin console and forwards the real client in
//     X-Forwarded-For, and
//   * DIRECTLY by the customer mobile application, which calls the public address over the
//     internet -- so the connection is fronted by the platform's own edge and the address on it is
//     a piece of that edge, identical for every customer on earth.
//
// The rate limiter partitions on the result (see ClientAddress below); without the header, one
// caller's brute-force attempt would spend the budget of every other tenant behind the same proxy.
//
// The header is therefore a SECURITY INPUT, and it is trusted only from the addresses named in
// KnownProxies. The subtlety that made this a live vulnerability: ForwardedHeadersMiddleware only
// verifies the sender when it has something to verify against —
//     checkKnownIps = KnownNetworks.Count > 0 || KnownProxies.Count > 0
// — so leaving BOTH lists empty does not mean "trust nobody", it means "trust anybody". With an
// empty KnownProxies any caller could name its own partition key and walk around every limit by
// rotating the header, including the 10-per-15-minutes cap that is the only brute-force protection
// on password sign-in. Read once, here, so the pipeline below can decline to enable the middleware
// at all rather than silently falling back into that state.
//
// Each entry may itself be a comma-separated list, because the correct list behind a managed edge
// runs to roughly twenty-five ranges and twenty-five KnownProxies__N variables typed into a
// dashboard is a configuration nobody re-reads, where one silent omission degrades the whole thing.
var knownProxies = (builder.Configuration.GetSection("KnownProxies").Get<string[]>() ?? [])
    .SelectMany(entry => entry.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    .ToArray();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // How many proxies stand in front of this API, and therefore how many entries of
    // X-Forwarded-For to believe. Counted from the RIGHT: the rightmost entry is the one the
    // nearest proxy appended, and each additional hop reads one further left.
    //
    // One is right when the BFF is the only thing in front, which is the case on a single host
    // with a proxy that this deployment owns. It is WRONG on a managed platform: Render, for
    // one, fronts a service with Cloudflare and its own load balancer, so at one hop the address
    // resolved is a piece of Render's infrastructure -- identical for every visitor. That does
    // not open the spoofing hole the guard below exists to close, but what it does instead is
    // worse than the "everyone shares one bucket" this comment used to claim:
    //
    //   * CredentialSubject.PartitionKey keys an auth request on {address}|{account}, so with the
    //     address constant the sign-in cap becomes a PER-ACCOUNT bucket shared between attacker
    //     and victim. Ten requests a quarter hour, aimed at a named administrator, holds that
    //     account shut indefinitely, and the victim cannot move out of the way.
    //   * the address-only limits -- GlobalLimiter, Refresh, the public browsing ceiling -- do
    //     collapse into one bucket for every visitor at once.
    //   * every session records that same address, so "Where you are signed in" tells nobody
    //     anything, including whoever is trying to measure this setting.
    //
    // It is a CEILING, not a target: trust is re-checked at each hop against the address just
    // consumed, so the walk stops at the first address no configured range covers however high
    // this is set. Raising it alone therefore does nothing -- measured against the real chain,
    // 1, 2 and 3 all resolved the same address while only the loopback hop was trusted. It must
    // equal the hop count exactly, and the reason not to simply set it high is narrow but real:
    // when the visitor's own address falls inside a trusted range, one hop too many consumes a
    // value the visitor supplied and lets them choose their own partition.
    //
    // Configurable rather than fixed because the answer is a property of where this is deployed,
    // not of the code, and getting it wrong is not visible until someone is locked out. Set it to
    // the number of proxies actually in front, having MEASURED the header rather than assumed it.
    options.ForwardLimit = builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var entry in knownProxies)
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

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddHealthChecks()
    .AddNpgSql(
        Khadra.Infrastructure.DependencyInjection.ResolveConnectionString(builder.Configuration),
        name: "postgres",
        tags: ["ready"]);

var app = builder.Build();

// Refuse to run in a state where the limiter cannot tell callers apart. With no trusted proxy there
// are only two possible behaviours and both are wrong for production: enable the middleware and the
// header is honoured from ANY caller (the bypass this guard exists to close), or leave it off and
// every request partitions on the BFF's single address, so ten failed sign-ins anywhere lock the
// whole platform out for fifteen minutes. Neither is a thing to discover after launch, so a
// deployment that has not named its proxy does not start.
if (knownProxies.Length == 0 && !app.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "KnownProxies is empty. The API must be told which address the BFF connects from, or it " +
        "cannot trust X-Forwarded-For and cannot tell one client from another when rate limiting. " +
        "Set the \"KnownProxies\" configuration array to the BFF's address(es).");
}

// Production must not run on the transport that delivers to nobody.
//
// It is the DEFAULT -- EmailOptions.Provider is "Logging" -- so forgetting the variable selects it,
// and until now nothing stopped that. The platform then behaves exactly as though mail worked:
// registration succeeds, an invitation "is sent", a password reset answers 202, and every message is
// written to this log and delivered to no one. It cost days of production debugging to notice, and
// the thing that hid it was the startup line reading "Email ready" for this very transport.
//
// IsProduction, deliberately, rather than the !IsDevelopment shape the guard above uses: a test host
// legitimately has no mail server, and ApiSmokeTests and ForwardedHeaderTrustTests boot as "Testing"
// and "Staging" on this transport. Render leaves ASPNETCORE_ENVIRONMENT unset, which defaults to
// Production -- so this bites exactly where it should.
//
// Fatal, unlike MailStartupCheck. That check is about a transport being briefly unreachable, which
// must not stop the API serving everything else. This is a configuration error fixed by setting one
// variable, and it is the same category as an empty proxy list: better refused at boot than
// discovered by somebody waiting at an inbox.
if (app.Environment.IsProduction())
{
    var mailProvider = builder.Configuration[$"{EmailOptions.SectionName}:Provider"]?.Trim();
    if (string.IsNullOrEmpty(mailProvider)
        || string.Equals(mailProvider, EmailOptions.LoggingProvider, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Email:Provider selects the Logging transport, which writes every message to the log and " +
            "delivers nothing. That is the default when the setting is missing, so this is most " +
            "likely an unset variable. Set Email__Provider to 'Brevo' (with Email__ApiKey beginning " +
            "'xkeysib-' and Email__FromAddress set to a sender Brevo has confirmed), 'Resend', or " +
            "'Smtp'. Set it to the bare word, with no surrounding quotes.");
    }
}

// Report the forwarding facts for the requests that can actually tell us something.
//
// Registered BEFORE UseForwardedHeaders, which is the whole point: that middleware CONSUMES the
// entry it uses -- removing it from X-Forwarded-For and overwriting RemoteIpAddress -- so a version
// registered after it, as this one was, reports the address the middleware decided on while calling
// it "the connection came from", which is the one thing an operator reading it needs to be true.
//
// The trigger matters as much as the position. Logging the FIRST request spends the one shot on a
// platform health probe: a loopback connection carrying no forwarding headers at all, arriving
// before any real traffic. That happened on the BFF, and the line it produced would have argued for
// trusting a health check. So: skip loopback probes, keep auth requests, and cap the rest.
var forwardingLinesLogged = 0;
app.Use(async (context, next) =>
{
    var peerAddress = context.Connection.RemoteIpAddress;
    var fromLoopback = peerAddress is not null && IPAddress.IsLoopback(peerAddress);
    var isAuth = context.Request.Path.StartsWithSegments("/api/v1/auth", StringComparison.OrdinalIgnoreCase);

    if ((!isAuth && fromLoopback) || Interlocked.Increment(ref forwardingLinesLogged) > 8)
    {
        await next(context).ConfigureAwait(false);
        return;
    }

    // Read on the way IN, before any of it is rewritten. Every forwarding header verbatim rather
    // than the one that was guessed at: an edge sending CF-Visitor but not X-Forwarded-Proto is
    // otherwise indistinguishable from an edge sending neither.
    var transportPeer = peerAddress?.ToString() ?? "(none)";
    var forwardingHeaders = string.Join(" | ", context.Request.Headers
        .Where(header =>
            header.Key.StartsWith("X-Forwarded", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase) ||
            header.Key.StartsWith("CF-", StringComparison.OrdinalIgnoreCase))
        .Select(header => header.Key + ": " + header.Value.ToString()));

    await next(context).ConfigureAwait(false);

    var headersText = forwardingHeaders.Length > 0 ? forwardingHeaders : "(no forwarding headers of any kind)";
    var path = context.Request.Path.Value ?? "/";
    var resolvedClient = context.Connection.RemoteIpAddress?.ToString() ?? "(none)";
    Program.LogForwarding(app.Logger, path, transportPeer, headersText, resolvedClient, context.Request.IsHttps);
});

// Only when there is a proxy to trust. Left off, RemoteIpAddress stays the true connection address:
// a poor partition key, but an honest one, and never one the caller chose.
if (knownProxies.Length > 0)
{
    app.UseForwardedHeaders();
    var trustedList = string.Join(", ", knownProxies);
    var forwardLimit = app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>()
        .Value.ForwardLimit;
    // Formatted before the call, and with the invariant culture: a hop count is a protocol
    // detail in a log line, not a number to be localised for whoever is reading.
    var forwardLimitText = forwardLimit?.ToString(CultureInfo.InvariantCulture) ?? "all";
    Program.LogTrustedProxies(app.Logger, trustedList, forwardLimitText);
}
else
{
    Program.LogNoTrustedProxy(app.Logger);
}

// Say out loud which database this is, and whether it can actually be reached.
//
// Without this, a wrong connection string is invisible until the first request that touches the
// database, which answers 500 with a deliberately opaque body while /health/live still says 200 and
// the platform reports a healthy deploy. That combination cost a production deployment most of a day:
// the readiness probe knew, but nothing said WHY, and the reason -- host, database, credentials, TLS
// -- is exactly what nobody can guess from outside.
//
// The password is never logged. Everything else is, because a connection string that names the wrong
// host or the wrong database is the common mistake and it cannot be diagnosed without seeing them.
//
// It does NOT stop the application. A database that is briefly unreachable at boot is a normal event
// on a managed platform, the readiness probe already reports it, and refusing to start would take
// away the one endpoint that still works while somebody is fixing the configuration.
await Program.ProbeDatabaseAsync(app.Services, app.Logger).ConfigureAwait(false);

// A standing check on the trust list, not a diagnostic to be pulled out later.
//
// Cloudflare sets Cf-Connecting-Ip to the address it accepted the connection from, overwriting
// whatever the caller sent, so on a request that genuinely came through Cloudflare it and the
// address walked out of X-Forwarded-For are the same fact reached two independent ways. They
// disagree in exactly the two ways this configuration fails, neither of which announces itself:
// a Cloudflare range missing from KnownProxies (the walk stops a hop short and every visitor
// behind that edge shares one bucket), or a request that never passed through Cloudflare carrying
// a forged header -- which is why this cross-checks the header and never believes it.
//
// Capped: a real misconfiguration says so in the first few requests, and a warning on every
// request is how a log stops being read.
var clientMismatchesLogged = 0;
app.Use(async (context, next) =>
{
    var claimed = context.Request.Headers["Cf-Connecting-Ip"].ToString();
    var resolved = context.Connection.RemoteIpAddress;

    if (claimed.Length > 0 && resolved is not null &&
        IPAddress.TryParse(claimed, out var claimedAddress) &&
        !SameAddress(claimedAddress, resolved) &&
        Interlocked.Increment(ref clientMismatchesLogged) <= 5)
    {
        var resolvedText = resolved.ToString();
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
        Program.LogClientAddressMismatch(
            app.Logger,
            claimed,
            resolvedText,
            forwardedFor.Length > 0
                ? forwardedFor
                : "(nothing left -- consumed by the walk, or never sent)");
    }

    await next(context).ConfigureAwait(false);
});

// An IPv4 address that arrived over a dual-stack socket comes back as ::ffff:a.b.c.d -- the same
// host written another way. Comparing the strings would call every request a mismatch.
static bool SameAddress(IPAddress claimed, IPAddress resolved) =>
    claimed.Equals(resolved) ||
    (resolved.IsIPv4MappedToIPv6 && claimed.Equals(resolved.MapToIPv4())) ||
    (claimed.IsIPv4MappedToIPv6 && claimed.MapToIPv4().Equals(resolved));
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
    app.UseHsts();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options.WithTitle("Khadra API")).AllowAnonymous();

    // Nothing is seeded. There used to be a DevelopmentSeeder here that invented administrators,
    // galleries, customers and bookings so the console had something to show; it is gone. Fabricated
    // accounts are not a development convenience — a seeded administrator can approve a real gallery,
    // its password was a working credential in source, and it counted towards the guard that is
    // supposed to stop the last real administrator being deactivated. Every account and every
    // gallery now arrives the way a real one does: through the screens.
    if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.AutoMigrate)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KhadraDbContext>().Database.MigrateAsync();
    }
}

// Every environment, not just Development, and after any migration: this is the only way a platform
// that has never had an administrator gets one, because inviting an administrator requires being
// one. A no-op unless Admin:Bootstrap is configured AND no administrator has ever existed.
using (var bootstrapScope = app.Services.CreateScope())
{
    await bootstrapScope.ServiceProvider
        .GetRequiredService<AdminBootstrapper>()
        .EnsureAsync();
}

// Says, on every start, whether mail will actually be delivered.
//
// Registration, the administrator invitation and every password reset depend on it, and until now
// the only way to find out it was misconfigured was for someone to register and wait at an inbox
// nothing was coming to. Never fatal: a mail outage must not stop the API serving everything else.
await MailStartupCheck.ReportAsync(app.Services);

// And whether a deposit can be taken. Today the answer is always no, because no provider is
// configured; the point of the line is that nobody has to discover it from a customer.
await PaymentsStartupCheck.ReportAsync(app.Services);

// Not in Development, and the reason is a device rather than a preference.
//
// A phone testing the customer app talks to this API over the local network, where there is no
// certificate it would trust and no HTTPS port bound to anything but localhost. With redirection on,
// every call from the phone answers 307 to an address it cannot reach, and the app reports the
// platform as unreachable. The Flutter debug build permits cleartext for exactly this case and the
// release build does not (android/app/src/debug/network_security_config.xml).
//
// Outside Development it is unconditional, and HSTS above it makes the browser stop trying HTTP at
// all after the first visit.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseCors();
// Before the limiter, because it partitions the auth endpoints by the account being named and the
// name is in the request body, which nothing has read at this point.
app.UseCredentialSubject();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

await app.RunAsync();

static string ClientAddress(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static FixedWindowRateLimiterOptions FixedWindow(int permitLimit, TimeSpan window, int queueLimit = 0) => new()
{
    PermitLimit = permitLimit,
    Window = window,
    QueueLimit = queueLimit,
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    AutoReplenishment = true
};

// Exposes the entry point to WebApplicationFactory in Khadra.Tests.
public partial class Program
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "KnownProxies is empty, so X-Forwarded-For is ignored and every request is rate " +
                  "limited against the address it arrives from. Behind the BFF that is ONE address " +
                  "shared by every client, so one caller's failed sign-ins spend everybody's budget. " +
                  "Name the BFF in KnownProxies.")]
    internal static partial void LogNoTrustedProxy(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Forwarding on {Path}. Transport peer {TransportPeer}. Headers: " +
                  "{ForwardingHeaders}. Resolved to client {ResolvedClient}, IsHttps {IsHttps}. " +
                  "If the resolved client equals the transport peer, or is the same platform " +
                  "address for every visitor, KnownProxies does not cover the hop that sent these " +
                  "headers and they are being ignored -- add the range containing it. If there are " +
                  "no forwarding headers at all, nothing in KnownProxies will help.")]
    internal static partial void LogForwarding(
        ILogger logger,
        string path,
        string transportPeer,
        string forwardingHeaders,
        string resolvedClient,
        bool isHttps);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "CLIENT ADDRESS DISAGREES WITH CLOUDFLARE. Cf-Connecting-Ip says {ClaimedClient} " +
                  "but the X-Forwarded-For walk resolved {ResolvedClient}, leaving {ForwardedFor}. " +
                  "Either a " +
                  "Cloudflare range is missing from KnownProxies -- in which case the walk stopped " +
                  "at a Cloudflare edge and every visitor behind it now shares one rate-limit " +
                  "bucket -- or this request reached the origin without passing through Cloudflare " +
                  "and its Cf-Connecting-Ip is forged. Check the published ranges at " +
                  "cloudflare.com/ips-v4 and /ips-v6 against KnownProxies before assuming the " +
                  "second.")]
    internal static partial void LogClientAddressMismatch(
        ILogger logger,
        string claimedClient,
        string resolvedClient,
        string forwardedFor);

    /// <summary>Opens one connection so a misconfigured database is a log line, not a mystery.</summary>
    internal static async Task ProbeDatabaseAsync(IServiceProvider services, ILogger logger)
    {
        var configuration = services.GetRequiredService<IConfiguration>();

        // Parsed and CONNECTED in two steps, so a failure can still say which host and which user it
        // was attempting. The first version logged only the reason, and "password authentication
        // failed for user \"postgres\"" does not answer the question that actually matters on a
        // pooled database: whether the username carried the project reference it needs. Naming the
        // attempt turns one more redeploy into a glance.
        string identity;
        string resolved;
        try
        {
            resolved = Khadra.Infrastructure.DependencyInjection.ResolveConnectionString(configuration);
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(resolved);
            identity =
                $"{builder.Host}:{builder.Port.ToString(CultureInfo.InvariantCulture)}, " +
                $"database {builder.Database ?? "(none)"}, as {builder.Username ?? "(none)"}";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The string could not even be read, so there is no host or user to name.
            LogDatabaseUnreachable(logger, "(the connection string could not be parsed)", exception.Message, exception);
            return;
        }

        try
        {
            await using var connection = new Npgsql.NpgsqlConnection(resolved);
            await connection.OpenAsync().ConfigureAwait(false);
            LogDatabaseReachable(logger, identity);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Deliberately broad: every way a connection can fail -- wrong host, refused, bad
            // credentials, TLS -- must produce the line rather than a second failure on top of the first.
            LogDatabaseUnreachable(logger, identity, exception.Message, exception);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Database reachable at {Identity}.")]
    internal static partial void LogDatabaseReachable(ILogger logger, string identity);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "DATABASE UNREACHABLE. Every request that reads or writes data will answer 500 and " +
                  "/health/ready will report unhealthy, while /health/live stays 200. Set " +
                  "ConnectionStrings__DefaultConnection to this deployment's database; both " +
                  "postgres:// URL form and Npgsql keyword form are accepted. Tried {Identity}. " +
                  "The failure was: {Reason}")]
    internal static partial void LogDatabaseUnreachable(ILogger logger, string identity, string reason, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Rate limiting will identify clients by X-Forwarded-For, trusted only from: " +
                  "{Proxies}, reading {ForwardLimit} hop(s) from the right. If that resolves to a " +
                  "platform address rather than a visitor, every caller shares one limit.")]
    internal static partial void LogTrustedProxies(ILogger logger, string proxies, string forwardLimit);
}
