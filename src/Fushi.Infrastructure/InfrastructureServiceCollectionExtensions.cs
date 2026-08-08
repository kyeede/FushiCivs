using Fushi.Application.Abstractions.Persistence;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Infrastructure.Options;
using Fushi.Infrastructure.Persistence;
using Fushi.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Fushi.Infrastructure;

/// <summary>
/// Registers persistence with a dependency injection container.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds the database session and the repositories.
    /// </summary>
    /// <remarks>
    /// Options are validated at startup rather than on first use, so a missing
    /// connection string stops the process with a clear message instead of surfacing
    /// the first time somebody runs a command.
    /// </remarks>
    /// <param name="services">The container to add to.</param>
    /// <param name="configuration">The configuration to bind options from.</param>
    /// <returns>
    /// <paramref name="services"/>, so registration can be chained.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddOptions(services, configuration);
        AddPersistence(services);

        // TimeProvider is how every handler reads the clock. Registered here rather
        // than in the application layer so that a test can substitute a fake without
        // the layer under test knowing a real one exists.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SECTION))
            .PostConfigure(options =>
            {
                // The connection string conventionally lives under ConnectionStrings,
                // which is where container platforms and `dotnet user-secrets` expect
                // to put it. The Database section can still override it, which is
                // useful for a test that needs its own database.
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    options.ConnectionString =
                        configuration.GetConnectionString("Database") ?? string.Empty;
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddDbContext<FushiDbContext>((provider, builder) =>
        {
            DatabaseOptions options = provider
                .GetRequiredService<IOptions<DatabaseOptions>>()
                .Value;

            builder.UseNpgsql(options.ConnectionString, npgsql =>
            {
                npgsql.CommandTimeout(options.CommandTimeoutSeconds);

                // Transient faults (network blips, brief unavailability) are
                // retried by the provider rather than surfaced as command failures.
                npgsql.EnableRetryOnFailure(
                    options.MaxRetryCount,
                    TimeSpan.FromSeconds(options.MaxRetryDelaySeconds),
                    errorCodesToAdd: null);

                npgsql.MigrationsHistoryTable(FushiDbContext.MIGRATIONS_HISTORY_TABLE);
            });

            builder.EnableSensitiveDataLogging(options.EnableSensitiveDataLogging);
            builder.EnableDetailedErrors(options.EnableSensitiveDataLogging);
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IGuildRepository, GuildRepository>();
        services.AddScoped<ICycleRepository, CycleRepository>();
        services.AddScoped<ISubmissionRepository, SubmissionRepository>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IShortCodeAllocator, ShortCodeAllocator>();
    }
}
