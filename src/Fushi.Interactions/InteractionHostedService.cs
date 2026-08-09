using System.Reflection;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using Fushi.Core.Errors;
using Fushi.Interactions.Formatting;
using Fushi.Interactions.Logging;
using Fushi.Interactions.Options;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fushi.Interactions;

/// <summary>
/// Loads the command modules, registers them with Discord, and routes every
/// arriving interaction to whichever one claims it.
/// </summary>
/// <remarks>
/// Registration waits for the gateway rather than happening at startup, because
/// the REST call that uploads a command set needs the bot's own application
/// identifier, and that arrives with the <c>Ready</c> frame. It also happens once
/// per process rather than once per <c>Ready</c>: Discord.Net raises that event
/// again after a resumed session, and re-uploading an unchanged command set on
/// every reconnect spends a strict rate limit on nothing.
/// <br/>
/// Handling runs on a task of its own rather than inline. Discord.Net raises
/// events on the gateway's own loop, so an interaction that waits on a database
/// would delay the heartbeat and eventually drop the connection — the work has to
/// leave that loop immediately.
/// </remarks>
/// <param name="client">The connected socket client.</param>
/// <param name="interactions">Discord.Net's command framework.</param>
/// <param name="provider">
/// The root container. Module dependencies are resolved from a scope the
/// framework opens per execution, so a handler gets its own unit of work.
/// </param>
/// <param name="options">Where the commands should be registered.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class InteractionHostedService(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider provider,
    IOptions<InteractionOptions> options,
    ILogger<InteractionHostedService> logger)
    : IHostedService
{
    private int _registered;

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Loaded before the handlers are attached, so an interaction cannot
        // arrive against a framework that does not yet know any commands.
        await interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), provider);

        client.Ready += OnReadyAsync;
        client.InteractionCreated += OnInteractionAsync;
        interactions.InteractionExecuted += OnExecutedAsync;

        // Subscribing is not enough on its own. Hosted services start in
        // registration order, and the gateway's connects rather than waits, so a
        // fast connection can raise Ready before this method has run — after which
        // it will not be raised again until a reconnect, and the commands would
        // simply never go up. Registering here as well closes that window; the
        // guard inside makes the duplicate call a no-op.
        if (client.ConnectionState == ConnectionState.Connected)
        {
            await OnReadyAsync();
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.Ready -= OnReadyAsync;
        client.InteractionCreated -= OnInteractionAsync;
        interactions.InteractionExecuted -= OnExecutedAsync;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Uploads the command set the first time the gateway reports ready.
    /// </summary>
    /// <remarks>
    /// A development guild is registered to directly, which takes effect at once.
    /// Without one the commands go up globally, which Discord may take up to an
    /// hour to propagate — fine for a deployment, unbearable while iterating.
    /// </remarks>
    /// <returns>A task that completes once registration has been attempted.</returns>
    private async Task OnReadyAsync()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        try
        {
            if (options.Value.DevelopmentGuildId is { } guildId)
            {
                await interactions.RegisterCommandsToGuildAsync(guildId);
                InteractionLog.RegisteredToGuild(
                    logger,
                    interactions.SlashCommands.Count,
                    guildId);
            }
            else
            {
                await interactions.RegisterCommandsGloballyAsync();
                InteractionLog.RegisteredGlobally(logger, interactions.SlashCommands.Count);
            }
        }
#pragma warning disable CA1031 // Catching everything is the requirement here, not
        // an oversight: this runs on the gateway's event loop, where an escaping
        // exception becomes an unobserved task rather than anything anybody sees.
        // The bot is still worth running with a stale command set, because the
        // previously registered commands keep working. The suppression is scoped
        // to this one handler rather than the file.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            InteractionLog.RegistrationFailed(logger, exception);

            // Put back so the next Ready — after a reconnect — tries again.
            Interlocked.Exchange(ref _registered, 0);
        }
    }

    private Task OnInteractionAsync(SocketInteraction interaction)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                SocketInteractionContext context = new(client, interaction);

                await interactions.ExecuteCommandAsync(context, provider);
            }
#pragma warning disable CA1031 // The same reasoning as above: this is the top of
            // its own task, so nothing above it catches and an exception here is
            // lost rather than handled. A user who pressed a button deserves to be
            // told it did not work.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                InteractionLog.InteractionThrew(
                    logger,
                    interaction.Id,
                    interaction.Type.ToString(),
                    exception);

                await ApologiseAsync(interaction, Unexpected);
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reports a failure the command framework caught before, or instead of, the
    /// module.
    /// </summary>
    /// <remarks>
    /// These are the failures a module never sees: an unparseable option, an
    /// unknown command, a precondition that refused. The module's own failures
    /// arrive as a <see cref="Core.Results.Result"/> and are already rendered by
    /// the time this runs, which is why a success is left entirely alone.
    /// </remarks>
    /// <param name="command">The command that ran, if one was found.</param>
    /// <param name="context">The interaction being answered.</param>
    /// <param name="result">What the framework made of it.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    private async Task OnExecutedAsync(
        ICommandInfo command,
        IInteractionContext context,
        IResult result)
    {
        if (result.IsSuccess)
        {
            return;
        }

        InteractionLog.InteractionFailed(
            logger,
            context.Interaction.Id,
            context.Interaction.Type.ToString(),
            result.ErrorReason);

        await ApologiseAsync(context.Interaction, Translate(result));
    }

    private static async Task ApologiseAsync(IDiscordInteraction interaction, Error error)
    {
        // An autocomplete interaction is answered with a list of choices and
        // nothing else. Replying to one with a message is not a thing Discord
        // allows, and an empty list is the only honest answer available.
        if (interaction.Type == InteractionType.ApplicationCommandAutocomplete)
        {
            return;
        }

        MessageComponent view = Replies.Error(error);

        if (interaction.HasResponded)
        {
            await interaction.FollowupAsync(
                components: view,
                ephemeral: true,
                flags: MessageFlags.ComponentsV2);
        }
        else
        {
            await interaction.RespondAsync(
                components: view,
                ephemeral: true,
                flags: MessageFlags.ComponentsV2);
        }
    }

    /// <summary>
    /// Turns the framework's failure into one this project knows how to say.
    /// </summary>
    /// <remarks>
    /// Only three of these are worth distinguishing to a user, and they are the
    /// three they can do something about: the command is gone, an option was
    /// wrong, or they are not allowed. The rest describe faults inside the bot and
    /// are all reported the same way, because "the argument converter threw" is
    /// not a sentence that helps anybody who is trying to vote.
    /// </remarks>
    /// <param name="result">What the framework made of the interaction.</param>
    /// <returns>The failure to show.</returns>
    private static Error Translate(IResult result) => result.Error switch
    {
        InteractionCommandError.UnknownCommand => Error.NotFound(
            "Interaction.UnknownCommand",
            "That command no longer exists. It may have been renamed — try typing `/` again to "
            + "see what is available."),
        InteractionCommandError.ConvertFailed or InteractionCommandError.BadArgs =>
            Error.Validation(
                "Interaction.BadArguments",
                "One of the options could not be read. Check the values and try again."),
        InteractionCommandError.ParseFailed => Error.Validation(
            "Interaction.ParseFailed",
            "That command could not be read. Try typing `/` and picking it from the list."),
        InteractionCommandError.UnmetPrecondition => Error.Forbidden(
            "Interaction.Forbidden",
            result.ErrorReason),
        InteractionCommandError.Exception or InteractionCommandError.Unsuccessful or null =>
            Unexpected,
        _ => Unexpected,
    };

    private static Error Unexpected => Error.Unexpected(
        "Interaction.Unexpected",
        "Something went wrong handling that. It has been logged; try again shortly.");
}
