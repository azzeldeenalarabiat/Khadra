using System.Text;
using Khadra.Application.Auditing.ReadModels;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Documents;
using Khadra.Infrastructure.Notifications;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Persistence.Seeding;
using Khadra.Infrastructure.PlatformSettings;
using Khadra.Infrastructure.Reporting;
using Khadra.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Khadra.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        AddOptions(services, configuration);
        AddPersistence(services, configuration);
        AddSecurity(services);
        AddNotifications(services, configuration);

        services.AddSingleton<IBusinessRulesProvider, ConfigurationBusinessRulesProvider>();
        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => Encoding.UTF8.GetByteCount(options.SigningKey ?? string.Empty) >= 32,
                "Authentication:Jwt:SigningKey must contain at least 32 bytes.")
            .ValidateOnStart();
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.RefreshFamilyDays >= options.RefreshTokenDays,
                "Authentication:Policy:RefreshFamilyDays must be at least RefreshTokenDays.")
            .ValidateOnStart();
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<DocumentStorageOptions>()
            .Bind(configuration.GetSection(DocumentStorageOptions.SectionName))
            .ValidateDataAnnotations()
            // Spec 7: these files must never be reachable as static content. Catching this at startup
            // is the difference between a config typo and every customer passport being public.
            .Validate(options => !options.RootPath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Contains("wwwroot", StringComparison.OrdinalIgnoreCase),
                "Documents:RootPath must not be inside wwwroot; document files are never served statically.")
            .Validate(options => options.AllowedContentTypes.Count > 0,
                "Documents:AllowedContentTypes must list at least one accepted type.")
            .ValidateOnStart();
        services.AddOptions<AdminDashboardOptions>()
            .Bind(configuration.GetSection(AdminDashboardOptions.SectionName))
            .ValidateDataAnnotations()
            // A bad zone id would otherwise surface as an exception on the first dashboard request
            // rather than at startup, and every figure on the screen depends on it.
            .Validate(options => TimeZoneInfo.TryFindSystemTimeZoneById(options.ReportingTimeZone, out _),
                "AdminDashboard:ReportingTimeZone must be a time zone this machine knows.")
            .ValidateOnStart();
        services.AddOptions<BusinessRulesOptions>()
            .Bind(configuration.GetSection(BusinessRulesOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.DealerNonDeliveryPenaltyMaxPercent >= options.DealerNonDeliveryPenaltyMinPercent,
                "BusinessRules: the maximum non-delivery penalty must be at least the minimum.")
            // Spec 2.1 collects commission out of the card deposit, so a commission above the deposit
            // would leave the platform chasing every dealer for the difference on every booking.
            .Validate(options => options.DepositPercent >= options.CommissionPercent,
                "BusinessRules: DepositPercent must be at least CommissionPercent.")
            .ValidateOnStart();
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is required.");

        services.AddDbContext<KhadraDbContext>(options =>
            options
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(KhadraDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IVerificationTokenRepository, VerificationTokenRepository>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<IDealerRepository, DealerRepository>();
        services.AddScoped<DevelopmentSeeder>();

        AddReporting(services);
    }

    // Read-side ports. Each bounded context publishes its own dashboard contract and this is where the
    // one implementation of it lives; extracting a context later means swapping the implementation for
    // an HTTP client, with no change above this line.
    private static void AddReporting(IServiceCollection services)
    {
        services.AddScoped<IDealerDashboardReader, DealerDashboardReader>();
        services.AddScoped<IBookingDashboardReader, BookingDashboardReader>();
        services.AddScoped<ICustomerDashboardReader, CustomerDashboardReader>();
        services.AddScoped<IDisputeDashboardReader, DisputeDashboardReader>();
        services.AddScoped<IAuditFeedReader, AuditFeedReader>();
        services.AddSingleton<IReportingCalendar, ReportingCalendar>();
        services.AddSingleton<IAdminDashboardSettings, AdminDashboardSettings>();
    }

    private static void AddSecurity(IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IOpaqueTokenService, OpaqueTokenService>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IAuthPolicySettings, AuthPolicySettings>();
        services.AddSingleton<IDocumentPolicySettings, DocumentPolicySettings>();
        services.AddSingleton<IDocumentStorage, LocalDocumentStorage>();
        services.AddSingleton<IDocumentLinkSigner, HmacDocumentLinkSigner>();
    }

    private static void AddNotifications(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration[$"{EmailOptions.SectionName}:Provider"] ?? EmailOptions.LoggingProvider;
        if (string.Equals(provider, EmailOptions.SmtpProvider, StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, LoggingEmailSender>();

        services.AddSingleton<IAuthEmailComposer, AuthEmailComposer>();
    }
}
