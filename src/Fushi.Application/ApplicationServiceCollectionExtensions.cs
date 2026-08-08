using System.Reflection;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Behaviors;
using Fushi.Application.Dispatching;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fushi.Application;

/// <summary>
/// Registers this layer with a dependency injection container.
/// </summary>
/// <remarks>
/// Handlers and validators are discovered by scanning this assembly rather than
/// listed by hand. Twenty-odd handlers listed individually is twenty chances to
/// add an operation and forget to register it, and the failure would appear only
/// when somebody ran the command.
/// <br/>
/// Scanning one known assembly once at startup is not the reflection cost that
/// gets criticised: it happens before the first request, and
/// <see cref="Dispatcher"/> caches everything it needs from it afterwards.
/// </remarks>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Adds the dispatcher, every handler, every validator, and the pipeline.
    /// </summary>
    /// <remarks>
    /// The caller must still supply the outward-facing interfaces this layer
    /// declares — <see cref="Abstractions.Persistence.IUnitOfWork"/>, the
    /// repositories, and the Discord abstractions. They are the infrastructure's
    /// to provide, and leaving them out here is what keeps this layer testable
    /// without one.
    /// </remarks>
    /// <param name="services">The container to add to.</param>
    /// <returns>
    /// <paramref name="services"/>, so registration can be chained.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Assembly assembly = typeof(ApplicationServiceCollectionExtensions).Assembly;

        services.TryAddScoped<IDispatcher, Dispatcher>();

        AddHandlers(services, assembly);
        AddValidators(services, assembly);

        // Order is the pipeline's order, outermost first. See IPipelineBehavior
        // for why it is this way round.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkBehavior<,>));

        return services;
    }

    private static void AddHandlers(IServiceCollection services, Assembly assembly)
    {
        foreach (Type implementation in Concretes(assembly))
        {
            foreach (Type contract in ClosedGenericsOf(implementation, typeof(IRequestHandler<,>)))
            {
                // Scoped, because a handler shares its unit of work with the
                // request it belongs to and must not outlive it.
                services.AddScoped(contract, implementation);
            }
        }
    }

    private static void AddValidators(IServiceCollection services, Assembly assembly)
    {
        foreach (Type implementation in Concretes(assembly))
        {
            foreach (Type contract in ClosedGenericsOf(implementation, typeof(IValidator<>)))
            {
                services.AddScoped(contract, implementation);
            }
        }
    }

    private static IEnumerable<Type> Concretes(Assembly assembly)
        => assembly.GetTypes().Where(static type => type is
        {
            IsClass: true,
            IsAbstract: false,
            IsGenericTypeDefinition: false,
        });

    private static IEnumerable<Type> ClosedGenericsOf(Type implementation, Type openContract)
        => implementation.GetInterfaces().Where(contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == openContract);
}
