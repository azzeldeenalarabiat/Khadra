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
using Khadra.Infrastructure.Geocoding;
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
        AddDocumentStorage(services, configuration);
        AddNotifications(services, configuration);
        AddGeocoding(services, configuration);

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
        services.AddScoped<IDocumentAccessLog, DocumentAccessLog>();
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
        services.AddSingleton<IDocumentLinkSigner, HmacDocumentLinkSigner>();
        services.AddSingleton<IUploadTicketService, HmacUploadTicketService>();
    }

    /// <summary>
    /// Where sensitive documents live: this machine's disk, or a private Supabase bucket.
    /// </summary>
    /// <remarks>
    /// Local is the default and stays the answer for development and the tests. Production sets
    /// Supabase, because a container's filesystem is not storage: it is deleted on every deploy, and
    /// with it every licence scan and every customer's identity document.
    ///
    /// Nothing about the port, the link signer or the controllers changes between the two. A document
    /// is reached the same way either way — a link this platform signed, checked by this platform,
    /// streamed by this platform. The bucket is private and no signed URL is ever minted.
    /// </remarks>
    private static void AddDocumentStorage(IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration[$"{DocumentStorageOptions.SectionName}:Provider"];
        var provider = configured?.Trim() ?? DocumentStorageOptions.LocalProvider;

        if (string.Equals(provider, DocumentStorageOptions.SupabaseProvider, StringComparison.OrdinalIgnoreCase))
        {
            var supabase = configuration
                .GetSection($"{DocumentStorageOptions.SectionName}:{nameof(DocumentStorageOptions.Supabase)}")
                .Get<SupabaseStorageOptions>() ?? new SupabaseStorageOptions();

            // All three, named individually. "Supabase storage is misconfigured" sends somebody
            // hunting through four settings; naming the missing one ends the search.
            var missing = new[]
            {
                string.IsNullOrWhiteSpace(supabase.Url) ? $"{DocumentStorageOptions.SectionName}:Supabase:Url" : null,
                string.IsNullOrWhiteSpace(supabase.Bucket) ? $"{DocumentStorageOptions.SectionName}:Supabase:Bucket" : null,
                string.IsNullOrWhiteSpace(supabase.ServiceKey) ? $"{DocumentStorageOptions.SectionName}:Supabase:ServiceKey" : null,
            }.Where(name => name is not null).ToList();

            if (missing.Count > 0)
            {
                // Fatal, and fatal at BOOT. The alternative is a platform that starts, serves every
                // other screen, and fails only when somebody uploads a licence scan -- which is the
                // failure this whole change exists to stop happening quietly.
                throw new InvalidOperationException(
                    $"{DocumentStorageOptions.SectionName}:Provider is " +
                    $"'{DocumentStorageOptions.SupabaseProvider}' but these are not set: " +
                    $"{string.Join(", ", missing)}. The bucket must already exist and must be PRIVATE, " +
                    "and the key must be the project's service_role key.");
            }

            if (!Uri.TryCreate(supabase.Url, UriKind.Absolute, out var baseUrl))
            {
                throw new InvalidOperationException(
                    $"{DocumentStorageOptions.SectionName}:Supabase:Url is not an absolute URL. It is the " +
                    "project address, e.g. https://abcdefgh.supabase.co");
            }

            // https everywhere except loopback. The rule exists because identity documents must not
            // cross a network in the clear -- and a request to 127.0.0.1 crosses no network at all,
            // which is what makes a local stand-in testable without loosening the rule that matters.
            // Any other host, including one on a private network, must be https.
            if (!string.Equals(baseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !baseUrl.IsLoopback)
            {
                throw new InvalidOperationException(
                    $"{DocumentStorageOptions.SectionName}:Supabase:Url must be https. Identity documents " +
                    "and licence scans do not travel over plain HTTP. (http is accepted only for a " +
                    "loopback address, where there is no network to travel over.)");
            }

            // The nested object is NOT covered by ValidateDataAnnotations -- it does not recurse -- so
            // its own [Range] never runs and a zero would surface as an exception on the first upload.
            if (supabase.TimeoutSeconds is < 1 or > 120)
            {
                throw new InvalidOperationException(
                    $"{DocumentStorageOptions.SectionName}:Supabase:TimeoutSeconds must be between 1 and 120.");
            }

            // The bucket is interpolated into a URL path, so it is held to the shape Supabase itself
            // allows rather than trusted.
            if (!System.Text.RegularExpressions.Regex.IsMatch(supabase.Bucket!, @"^[a-z0-9][a-z0-9._-]{1,99}$"))
            {
                throw new InvalidOperationException(
                    $"{DocumentStorageOptions.SectionName}:Supabase:Bucket may contain only lowercase " +
                    "letters, digits, dot, underscore and hyphen.");
            }

            services.AddHttpClient(SupabaseDocumentStorage.HttpClientName, client =>
            {
                // Every path in the provider is relative to this, so the trailing slash is load-bearing:
                // without it, Uri resolution drops the /storage/v1 segment and every call 404s.
                client.BaseAddress = new Uri(baseUrl, "/storage/v1/");
                client.Timeout = TimeSpan.FromSeconds(supabase.TimeoutSeconds);

                // apikey ALWAYS; Authorization only for a JWT.
                //
                // Supabase is retiring the legacy `service_role` JWT in favour of `sb_secret_…` keys,
                // and the new ones are REFUSED if sent as a Bearer token -- storage-api tries to parse
                // it as a JWT and answers "Invalid JWT". Sending the bearer header only when the key
                // actually is a JWT means both kinds work, and the platform does not acquire a
                // deprecation deadline it will meet by breaking.
                client.DefaultRequestHeaders.Add("apikey", supabase.ServiceKey);
                if (supabase.ServiceKey!.StartsWith("eyJ", StringComparison.Ordinal))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", supabase.ServiceKey);
                }
            })
            // Trace-level HttpClient logging prints request headers verbatim, and one of these is a
            // key that can read every object in the project.
            .RedactLoggedHeaders(["apikey", "Authorization"]);

            services.AddSingleton<IDocumentStorage, SupabaseDocumentStorage>();
            services.AddSingleton<IDocumentStoreProbe, SupabaseStoreProbe>();
            return;
        }

        if (!string.Equals(provider, DocumentStorageOptions.LocalProvider, StringComparison.OrdinalIgnoreCase)
            && provider.Length > 0)
        {
            // The same silent-fallback trap the mail transport had, and it would be worse here: an
            // unrecognised name would quietly choose the store that loses everything on redeploy.
            throw new InvalidOperationException(
                $"{DocumentStorageOptions.SectionName}:Provider is \"{configured}\", which is not a store " +
                $"this API has. Use '{DocumentStorageOptions.LocalProvider}' or " +
                $"'{DocumentStorageOptions.SupabaseProvider}'.");
        }

        services.AddSingleton<IDocumentStorage, LocalDocumentStorage>();
        services.AddSingleton<IDocumentStoreProbe, LocalStoreProbe>();
    }

    /// <summary>
    /// The reverse-geocoding proxy, which turns a map pin into words for a form to offer.
    /// </summary>
    /// <remarks>
    /// Server-side on purpose. The console's CSP is <c>connect-src 'self'</c>, so the browser cannot
    /// call a provider directly — and going through the API is the better answer anyway: the
    /// provider's rate limit is per SERVER, which only the server can enforce, and an owner's
    /// coordinates never leave this origin from their own browser.
    ///
    /// Absent by default, exactly as payments are. A platform that has not chosen a provider asks
    /// nobody, and the form says to type the address.
    /// </remarks>
    private static void AddGeocoding(IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration[$"{GeocodingOptions.SectionName}:Provider"];
        var provider = configured?.Trim() ?? GeocodingOptions.NoProvider;
        services.Configure<GeocodingOptions>(configuration.GetSection(GeocodingOptions.SectionName));

        if (string.Equals(provider, GeocodingOptions.NominatimProvider, StringComparison.OrdinalIgnoreCase))
        {
            var options = configuration.GetSection(GeocodingOptions.SectionName).Get<GeocodingOptions>()
                ?? new GeocodingOptions();

            // Refused at startup, not per request. Nominatim blocks callers that do not identify
            // themselves, and a block is indistinguishable from the feature never having worked --
            // every suggestion just fails, for everyone, with nothing saying why.
            if (string.IsNullOrWhiteSpace(options.UserAgent))
            {
                throw new InvalidOperationException(
                    $"{GeocodingOptions.SectionName}:Provider is '{GeocodingOptions.NominatimProvider}' " +
                    $"but {GeocodingOptions.SectionName}:UserAgent is not set. Nominatim's usage policy " +
                    "requires a User-Agent that identifies the application and gives a contact, and it " +
                    "blocks callers that do not — set something like " +
                    "\"Khadra/1.0 (+https://khadra.example; ops@khadra.example)\".");
            }

            services.AddMemoryCache();
            services.AddHttpClient(NominatimReverseGeocoder.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            });
            services.AddSingleton<IReverseGeocoder, NominatimReverseGeocoder>();
            return;
        }

        if (!string.Equals(provider, GeocodingOptions.NoProvider, StringComparison.OrdinalIgnoreCase)
            && provider.Length > 0)
        {
            // The same silent-fallback trap the email transport had: an unrecognised name used to
            // mean "the feature quietly does nothing".
            throw new InvalidOperationException(
                $"{GeocodingOptions.SectionName}:Provider is \"{configured}\", which is not a provider " +
                $"this API has. Use '{GeocodingOptions.NominatimProvider}' or " +
                $"'{GeocodingOptions.NoProvider}'.");
        }

        services.AddSingleton<IReverseGeocoder, UnconfiguredReverseGeocoder>();
    }

    private static void AddNotifications(IServiceCollection services, IConfiguration configuration)
    {
        // Four transports, one switch. `Resend` talks HTTPS and needs only an API key; `Smtp` covers
        // Gmail and any relay that speaks it (Brevo: smtp-relay.brevo.com:587, username = your login,
        // password = an SMTP key), so a second provider needs no code, only configuration.
        // Trimmed, because this arrives from an environment variable and a trailing space selects a
        // different transport than the one somebody typed. The match below is by string, and the
        // else-branch is silent by construction, so every way of getting it slightly wrong lands on
        // the transport that delivers nothing.
        var configuredProvider = configuration[$"{EmailOptions.SectionName}:Provider"];
        var provider = configuredProvider?.Trim() ?? EmailOptions.LoggingProvider;
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
        else if (provider.Length == 0
            || string.Equals(provider, EmailOptions.LoggingProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
            // Given the value that was read, so the startup line can say WHY this transport was
            // chosen rather than only that it was.
            services.AddSingleton<IEmailTransportProbe>(_ => new LoggingTransportProbe(configuredProvider));
        }
        else
        {
            // The else-branch used to be this one, silently. Every way of getting the name slightly
            // wrong -- a typo, a stray quote from a documentation example pasted whole into a
            // dashboard field, a trailing comma -- selected the transport that delivers to nobody,
            // and the platform then reported every registration and password reset as successful.
            //
            // The value is quoted so a space or a quote character inside it is visible; unquoted,
            // 'Brevo' and '"Brevo" ' look identical in a log.
            throw new InvalidOperationException(
                $"Email:Provider is \"{configuredProvider}\", which is not a transport this API has. " +
                $"Use one of: {EmailOptions.BrevoProvider}, {EmailOptions.ResendProvider}, " +
                $"{EmailOptions.SmtpProvider}, {EmailOptions.LoggingProvider}. Note the quotes around " +
                "the value above are this message's own -- if the value inside them has quotes or " +
                "spaces of its own, that is the problem: set the variable to the bare word.");
        }

        services.AddSingleton<IAuthEmailComposer, AuthEmailComposer>();
    }
}
