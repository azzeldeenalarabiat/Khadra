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
builder.Logging.AddJsonConsole();
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
});

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection($"{AppOptions.SectionName}:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0)
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

// The API answers the BFF, never a browser directly, so the address on the connection is always the
// BFF's. The BFF forwards the real client in X-Forwarded-For and the rate limiter partitions on the
// result (see ClientAddress below); without that, one caller's brute-force attempt would spend the
// budget of every other tenant behind the same proxy.
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
var knownProxies = builder.Configuration.GetSection("KnownProxies").Get<string[]>() ?? [];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // One hop. The BFF is the only proxy in front of the API, so only the address IT appends is
    // read; anything the caller left further left in the header is ignored.
    options.ForwardLimit = 1;
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
        builder.Configuration.GetConnectionString(Khadra.Infrastructure.DependencyInjection.ConnectionStringName) ?? string.Empty,
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

// Only when there is a proxy to trust. Left off, RemoteIpAddress stays the true connection address:
// a poor partition key, but an honest one, and never one the caller chose.
if (knownProxies.Length > 0)
{
    app.UseForwardedHeaders();
    var trustedList = string.Join(", ", knownProxies);
    Program.LogTrustedProxies(app.Logger, trustedList);
}
else
{
    Program.LogNoTrustedProxy(app.Logger);
}
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
        Message = "Rate limiting will identify clients by X-Forwarded-For, trusted only from: {Proxies}.")]
    internal static partial void LogTrustedProxies(ILogger logger, string proxies);
}
