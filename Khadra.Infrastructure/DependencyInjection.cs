using Khadra.Domain.PlatformSettings.Repositories;
using System.Text;
using Khadra.Application.Auditing.ReadModels;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.Fleet.Repositories;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Domain.Payments.Repositories;
using Khadra.Domain.Reviews.Repositories;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Documents;
using Khadra.Infrastructure.Notifications;
using Khadra.Infrastructure.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.PlatformSettings;
using Khadra.Infrastructure.Reporting;
using Khadra.Infrastructure.Scheduling;
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

        // The timer behind pre-launch checklist item 4. Every rule it applies belongs to the Booking
        // aggregate; this only decides how often to ask.
        services.AddHostedService<BookingSettlementService>();

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
        // No validation: an empty section is the normal state once the platform has an administrator.
        services.AddOptions<AdminBootstrapOptions>()
            .Bind(configuration.GetSection(AdminBootstrapOptions.SectionName));
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
        services.AddOptions<DealerConsoleOptions>()
            .Bind(configuration.GetSection(DealerConsoleOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<SchedulingOptions>()
            .Bind(configuration.GetSection(SchedulingOptions.SectionName))
            .ValidateDataAnnotations()
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
            // Absence is a misconfiguration, not a default. Without this a deleted key binds to null,
            // the provider would have to invent a number, and a car would go straight back out with
            // no time to be cleaned. Zero remains a legitimate, deliberate value.
            .Validate(options => options.TurnaroundMinutes is not null,
                "BusinessRules: TurnaroundMinutes must be set. Use 0 to allow back-to-back rentals.")
            .Validate(options => options.MaxAdvanceBookingDays is not null,
                "BusinessRules: MaxAdvanceBookingDays must be set.")
            // Not merely present but positive. Zero would mean a car could be booked for one minute
            // from now, and every window on that booking -- the dealer's answer, the customer's
            // payment, free cancellation -- is capped at the rental start, so all three would
            // collapse while /app-config still advertised a 24-hour payment window.
            .Validate(options => options.MinimumBookingLeadTimeMinutes is > 0,
                "BusinessRules: MinimumBookingLeadTimeMinutes must be set to a positive number of minutes.")
            .Validate(options => options.MaxRentalDays is > 0,
                "BusinessRules: MaxRentalDays must be set to a positive number of days.")
            // Present, not positive: 0 is the owner's to choose and says a gallery is late at the
            // agreed minute. Absence is the misconfiguration, and it had no check at all until
            // 2026-09-08 -- the provider dereferences this with `!`, so a deleted key surfaced as a
            // NullReferenceException on the first booking priced, not at startup.
            .Validate(options => options.NonDeliveryGraceMinutes is not null,
                "BusinessRules: NonDeliveryGraceMinutes must be set. Use 0 to allow an immediate report.")
            // Positive, not merely present. Zero would reveal every review the instant it was written
            // and close the window before the other party could answer, which is the blind window
            // switched off by a typo rather than by a decision.
            .Validate(options => options.ReviewWindowDays is > 0,
                "BusinessRules: ReviewWindowDays must be set to a positive number of days.")
            .ValidateOnStart();
        services.AddOptions<PaymentOptions>()
            .Bind(configuration.GetSection(PaymentOptions.SectionName))
            .ValidateDataAnnotations()
            // A session that outlived the booking's own payment window would take money the platform
            // then has to give back. The margin has to be smaller than the session, or every checkout
            // would be refused before it opened.
            .Validate(options => options.CheckoutClosesBeforeDeadlineMinutes < options.CheckoutSessionMinutes,
                "Payments: CheckoutClosesBeforeDeadlineMinutes must be less than CheckoutSessionMinutes.")
            .ValidateOnStart();
    }

    /// <summary>
    /// The database this application talks to, in the form Npgsql understands.
    /// </summary>
    /// <remarks>
    /// Public because the health check needs the SAME value: a readiness probe testing a different
    /// connection string from the one the DbContext uses could report healthy while every request
    /// failed, which is worse than having no probe.
    /// </remarks>
    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is required.");

        return PostgresConnectionString.Normalise(configured);
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = ResolveConnectionString(configuration);

        services.AddDbContext<KhadraDbContext>(options =>
            options
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(KhadraDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IVehicleHoldLock, VehicleHoldLock>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IVerificationTokenRepository, VerificationTokenRepository>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<IDealerRepository, DealerRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IDisputeTicketRepository, DisputeTicketRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IProviderEventReceiptRepository, ProviderEventReceiptRepository>();
        services.AddScoped<INotifier, Notifier>();

        AddReporting(services);
    }

    // Read-side ports. Each bounded context publishes its own dashboard contract and this is where the
    // one implementation of it lives; extracting a context later means swapping the implementation for
    // an HTTP client, with no change above this line.
    private static void AddReporting(IServiceCollection services)
    {
        services.AddScoped<IDealerDashboardReader, DealerDashboardReader>();
        services.AddScoped<IDealerAdminReader, DealerAdminReader>();
        services.AddScoped<IEmployeeReader, EmployeeReader>();
        services.AddScoped<IDealerBookingReader, DealerBookingReader>();
        services.AddScoped<IDealerFleetReader, DealerFleetReader>();
        services.AddScoped<ICatalogueReader, CatalogueReader>();
        services.AddScoped<IBookingDashboardReader, BookingDashboardReader>();
        services.AddScoped<IBookingReader, BookingReader>();
        services.AddScoped<ICustomerDashboardReader, CustomerDashboardReader>();
        services.AddScoped<IDisputeDashboardReader, DisputeDashboardReader>();
        services.AddScoped<IAdminUserReader, AdminUserReader>();
        services.AddScoped<ISessionReader, SessionReader>();
        services.AddScoped<ICarTypeRepository, CarTypeRepository>();
        services.AddScoped<ICityRepository, CityRepository>();
        services.AddScoped<ICustomerAdminReader, CustomerAdminReader>();
        services.AddScoped<IDisputeAdminReader, DisputeAdminReader>();
        services.AddScoped<IAuditFeedReader, AuditFeedReader>();
        services.AddScoped<IGalleryReviewReader, GalleryReviewReader>();
        services.AddScoped<ICustomerReputationReader, CustomerReputationReader>();
        // The dashboard glance and the audit screen read one table with different questions: a fixed
        // seven-row feed, and a filtered, paged log. Two readers, deliberately.
        services.AddScoped<IAuditLogReader, AuditLogReader>();
        services.AddScoped<IAuditActorReader, AuditActorReader>();
        services.AddSingleton<IReportingCalendar, ReportingCalendar>();
        services.AddSingleton<IAdminDashboardSettings, AdminDashboardSettings>();
        services.AddSingleton<IDealerConsoleSettings, DealerConsoleSettings>();
        services.AddSingleton<IPaymentSettings, PaymentSettings>();
        // The ONLY implementation this build ships. See UnconfiguredPaymentProvider for why nothing
        // that simulates a successful capture may ever be registered here.
        services.AddSingleton<IPaymentProvider, UnconfiguredPaymentProvider>();
        services.AddSingleton<IPaymentProviderProbe, PaymentProviderProbe>();
    }

    private static void AddSecurity(IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IOpaqueTokenService, OpaqueTokenService>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IAuthPolicySettings, AuthPolicySettings>();
        services.AddSingleton<IAdminBootstrapSettings, AdminBootstrapSettings>();
        services.AddSingleton<IAccessTokenSettings, AccessTokenSettings>();
        services.AddSingleton<IDocumentPolicySettings, DocumentPolicySettings>();
        services.AddSingleton<IDocumentStorage, LocalDocumentStorage>();
        services.AddSingleton<IDocumentLinkSigner, HmacDocumentLinkSigner>();
        services.AddSingleton<IUploadTicketService, HmacUploadTicketService>();
    }

    private static void AddNotifications(IServiceCollection services, IConfiguration configuration)
    {
        // Four transports, one switch. `Resend` talks HTTPS and needs only an API key; `Smtp` covers
        // Gmail and any relay that speaks it (Brevo: smtp-relay.brevo.com:587, username = your login,
        // password = an SMTP key), so a second provider needs no code, only configuration.
        var provider = configuration[$"{EmailOptions.SectionName}:Provider"] ?? EmailOptions.LoggingProvider;
        if (string.Equals(provider, EmailOptions.ResendProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient(ResendEmailSender.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://api.resend.com/");
                // A registration waits on this call, so it fails fast rather than hanging the form.
                client.Timeout = TimeSpan.FromSeconds(15);
            });
            services.AddSingleton<IEmailSender, ResendEmailSender>();
            services.AddSingleton<IEmailTransportProbe, ResendTransportProbe>();
        }
        else if (string.Equals(provider, EmailOptions.BrevoProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient(BrevoEmailSender.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://api.brevo.com/");
                // A registration waits on this call, so it fails fast rather than hanging the form.
                client.Timeout = TimeSpan.FromSeconds(15);
                var apiKey = configuration[$"{EmailOptions.SectionName}:ApiKey"];
                if (!string.IsNullOrWhiteSpace(apiKey))
                    client.DefaultRequestHeaders.Add(BrevoEmailSender.ApiKeyHeader, apiKey);
            });
            services.AddSingleton<IEmailSender, BrevoEmailSender>();
            services.AddSingleton<IEmailTransportProbe, BrevoTransportProbe>();
        }
        else if (string.Equals(provider, EmailOptions.SmtpProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
            services.AddSingleton<IEmailTransportProbe, SmtpTransportProbe>();
        }
        else
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
            services.AddSingleton<IEmailTransportProbe, LoggingTransportProbe>();
        }

        services.AddSingleton<IAuthEmailComposer, AuthEmailComposer>();
    }
}
