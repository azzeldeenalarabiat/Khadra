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
        services.AddScoped<IdentityAccess.AdminUsers.AdminBootstrapper>();
        services.AddScoped<Auditing.AdminActionRecorder>();
        services.AddScoped<Dealers.DealerReviewAuditor>();
        services.AddScoped<Dealers.DealerMembershipResolver>();
        services.AddScoped<EmployeeAccountProvisioner>();
        services.AddScoped<Bookings.BookingPartyResolver>();
        services.AddScoped<Disputes.DisputeAuditor>();
        services.AddScoped<Disputes.DisputeViewComposer>();
        services.AddScoped<Notifications.DealerTeamNotifier>();
        services.AddScoped<AuthEmailDispatcher>();

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
