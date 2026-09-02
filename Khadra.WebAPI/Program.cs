using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Khadra.Application;
using Khadra.Application.Common;
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
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
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
    options.AddPolicy(RateLimitPolicies.Auth, context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientAddress(context), _ => FixedWindow(10, TimeSpan.FromMinutes(1))));
    options.AddPolicy(RateLimitPolicies.Login, context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientAddress(context), _ => FixedWindow(10, TimeSpan.FromMinutes(15))));
    options.AddPolicy(RateLimitPolicies.Refresh, context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientAddress(context), _ => FixedWindow(60, TimeSpan.FromMinutes(1))));
});

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection($"{AppOptions.SectionName}:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0)
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in builder.Configuration.GetSection("KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString(Khadra.Infrastructure.DependencyInjection.ConnectionStringName) ?? string.Empty,
        name: "postgres",
        tags: ["ready"]);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
    app.UseHsts();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options.WithTitle("Khadra API")).AllowAnonymous();

    if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.AutoMigrate)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KhadraDbContext>().Database.MigrateAsync();
    }
}

app.UseHttpsRedirection();
app.UseCors();
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
public partial class Program;
