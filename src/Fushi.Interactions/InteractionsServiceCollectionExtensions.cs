using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using Fushi.Application.Abstractions.Discord;
using Fushi.Interactions.Options;
using Fushi.Interactions.Publishing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fushi.Interactions;

/// <summary>
/// Registers the Discord surface with a dependency injection container.
/// </summary>
public static class InteractionsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the command framework, the modules that answer interactions, and the
    /// publisher that writes the bot's messages.
    /// </summary>
    /// <remarks>
    /// Expects the socket client to be registered already, by
    /// <c>AddGateway</c>. The two are kept apart because they answer different
    /// questions — the gateway owns the connection, this owns what is said over
    /// it — and because a bot that only reads intake could run the first without
    /// the second.
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
    public static IServiceCollection AddInteractions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<InteractionOptions>()
            .Bind(configuration.GetSection(InteractionOptions.SECTION));

        services.AddSingleton(provider => new InteractionService(
            provider.GetRequiredService<DiscordSocketClient>(),
            Configuration()));

        // A singleton, like the gateway's own adapters: it holds the client and a
        // logger, keeps no state between calls, and every embed it builds is
        // built from the entity it was handed.
        services.AddSingleton<IDiscordPublisher, DiscordPublisher>();

        services.AddHostedService<InteractionHostedService>();

        return services;
    }

    private static InteractionServiceConfig Configuration() => new()
    {
        // Async, so a handler that waits on the database does not hold up the
        // gateway's event loop. The framework opens a service scope per execution
        // regardless, which is what keeps a unit of work from being shared
        // between two people voting at the same moment.
        DefaultRunMode = RunMode.Async,
        AutoServiceScopes = true,

        // On, because a modal that arrives without one of its fields is a bug in
        // this project rather than something a user did, and quietly binding it
        // to null would store an empty reason as though somebody had typed one.
        ExitOnMissingModalField = true,

        // Compiled lambdas trade memory for speed on every command execution. The
        // command set is small and fixed, so the memory is a rounding error and
        // the speed is on the path a user is waiting on.
        UseCompiledLambda = true,

        LogLevel = LogSeverity.Info,
        LocalizationManager = null,
        RestResponseCallback = null,
        ThrowOnError = false,
    };
}
