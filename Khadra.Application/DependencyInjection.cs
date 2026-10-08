using System.Reflection;
using FluentValidation;
using Khadra.Application.Common.Behaviors;
using Khadra.Application.IdentityAccess;
using Khadra.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Khadra.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddScoped<AuthTokenFactory>();
        services.AddScoped<AccountRegistrar>();
        // Consent to the legal texts (Wave 4, W4-8): staged for the caller's own save, never saved here.
        services.AddScoped<Legal.LegalConsentRecorder>();
        services.AddScoped<IdentityAccess.AdminUsers.AdminBootstrapper>();
        services.AddScoped<Auditing.AdminActionRecorder>();
        services.AddScoped<Auditing.DocumentAccessRecorder>();
        services.AddScoped<Dealers.DealerReviewAuditor>();
        services.AddScoped<Dealers.DealerMembershipResolver>();
        services.AddScoped<EmployeeAccountProvisioner>();
        services.AddScoped<InvitationReissuer>();
        services.AddScoped<Bookings.BookingPartyResolver>();
        services.AddScoped<Payments.BookingPaymentAvailability>();
        services.AddScoped<Bookings.BookingPricer>();
        services.AddScoped<Disputes.DisputeAuditor>();
        services.AddScoped<Disputes.DisputeViewComposer>();
        services.AddScoped<Notifications.DealerTeamNotifier>();
        // The one place an expiry is announced (Wave 4, checklist 234): the settling seam, the sweep and a new request all use it.
        services.AddScoped<Bookings.BookingExpiryAnnouncer>();
        services.AddScoped<Bookings.Handover.HandoverVerifier>();
        services.AddScoped<AuthEmailDispatcher>();
        services.AddScoped<Bookings.BookingEmailDispatcher>();
        // Issued financial documents (payments Phase 5): the composer is pure, the other two share the scope.
        services.AddSingleton<FinancialDocuments.Composition.FinancialDocumentComposer>();
        // Which repeated sweep failures this process has already reported at Error (pre-launch item 236).
        services.AddSingleton<Common.RepeatedFailureLog>();
        services.AddScoped<FinancialDocuments.Issuance.DocumentPreparation>();
        services.AddScoped<FinancialDocuments.Issuance.FinancialDocumentIssuing>();

        var eventHandlerRegistrations = assembly
            .DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .SelectMany(type => type.ImplementedInterfaces
                .Where(contract => contract.IsGenericType &&
                                   contract.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>))
                .Select(contract => (Service: contract, Implementation: type.AsType())));

        foreach (var (service, implementation) in eventHandlerRegistrations)
            services.AddScoped(service, implementation);

        return services;
    }
}
