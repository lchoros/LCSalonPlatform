using System.Reflection;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Salon.Core.Cqrs;

namespace Salon.Web.Framework.Pipeline;

public static class SalonPipelineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the dispatcher, the behaviours (Logging → Validation → TenantGuard → Transaction → handler), every
    /// <see cref="IRequestHandler{TRequest, TResult}"/> and every FluentValidation validator found in
    /// <paramref name="handlerAssemblies"/>. Safe to call more than once; nothing is registered twice.
    /// </summary>
    public static IServiceCollection AddSalonPipeline(this IServiceCollection services, params Assembly[] handlerAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handlerAssemblies);

        services.TryAddScoped<IDispatcher, Dispatcher>();
        services.TryAddScoped<IUnitOfWork, EfUnitOfWork>();

        services.TryAddEnumerable(
        [
            ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>)),
            ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>)),
            ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(TenantGuardBehavior<,>)),
            ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>)),
        ]);

        foreach (var assembly in handlerAssemblies)
        {
            foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
            {
                foreach (var handlerInterface in type.GetInterfaces().Where(IsRequestHandler))
                {
                    var other = services.FirstOrDefault(d => d.ServiceType == handlerInterface && d.ImplementationType != type);
                    if (other is not null)
                    {
                        throw new InvalidOperationException(
                            $"Request '{handlerInterface.GenericTypeArguments[0].Name}' has two handlers: {other.ImplementationType!.FullName} and {type.FullName}. "
                            + "A request has exactly one handler (ADR 0003).");
                    }

                    services.TryAddEnumerable(ServiceDescriptor.Scoped(handlerInterface, type));
                }
            }
        }

        services.AddValidatorsFromAssemblies(
            handlerAssemblies,
            ServiceLifetime.Scoped,
            filter: result => !services.Any(d => d.ServiceType == result.InterfaceType && d.ImplementationType == result.ValidatorType),
            includeInternalTypes: true);

        return services;
    }

    private static bool IsRequestHandler(Type candidate)
        => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IRequestHandler<,>);
}