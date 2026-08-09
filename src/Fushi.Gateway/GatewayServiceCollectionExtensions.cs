using Fushi.Application.Abstractions.Discord;
using Fushi.Gateway.Adapters;
using Fushi.Gateway.Options;

using Discord.WebSocket;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Fushi.Gateway;

/// <summary>
/// Registers the connection to Discord with a dependency injection container.
/// </summary>
public static class GatewayServiceCollectionExtensions
{
    /// <summary>
    /// Adds the socket client, the service that owns its connection, and the
    /// adapters that read from it.
    /// </summary>
    /// <remarks>
    /// Options are validated at startup rather than on first use, so a missing bot
    /// token stops the process with a clear message instead of surfacing as a
    /// login failure seconds after the host has reported itself healthy.
    /// <br/>
    /// Nothing to do with presentation is registered here. Slash commands,
    /// embeds, and components belong to the interactions layer, which composes on
    /// top of the client this method supplies.
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
    public static IServiceCollection AddGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddOptions(services, configuration);
        AddClient(services);
        AddAdapters(services);

        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DiscordOptions>()
            .Bind(configuration.GetSection(DiscordOptions.SECTION))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    private static void AddClient(IServiceCollection services)
    {
        // The gateway measures how long an outage lasted, so it needs a clock. The
        // infrastructure layer registers the same instance; whichever runs first
        // wins and the other is a no-op, which is why this is a Try.
        services.TryAddSingleton(TimeProvider.System);

        // One client for the whole process. Discord allows a bot a single gateway
        // connection per shard, and a second client would not be a second
        // connection so much as an identify-rate-limit ban.
        services.AddSingleton(provider => DiscordClientFactory.Create(
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<DiscordSocketClient>()));

        // Registered concretely as well as behind its interface so the hosted
        // service can raise the signal while everything else can only wait on it.
        // Both resolve the same instance; a second registration of the concrete
        // type would give the waiters a signal nobody ever sets.
        services.AddSingleton<GatewayReadiness>();
        services.AddSingleton<IGatewayReadiness>(
            provider => provider.GetRequiredService<GatewayReadiness>());

        services.AddHostedService<GatewayHostedService>();
    }

    private static void AddAdapters(IServiceCollection services)
    {
        // Singletons rather than scoped: both hold nothing but the client and a
        // logger, and neither caches an answer, so there is nothing a per-request
        // instance would isolate.
        services.AddSingleton<IGuildMemberLookup, DiscordGuildMemberLookup>();
        services.AddSingleton<IGuildDirectory, DiscordGuildDirectory>();
        services.AddSingleton<IIntakeSource, DiscordIntakeSource>();
    }
}
