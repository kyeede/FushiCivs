using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Core.Errors;
using Fushi.Core.Results;
using Fushi.Interactions.Formatting;

namespace Fushi.Interactions.Modules;

/// <summary>
/// The shared behaviour of every module: dispatching a request and turning the
/// <see cref="Result"/> it returns into a reply.
/// </summary>
/// <remarks>
/// Every command defers before dispatching. Discord closes the window for a first
/// response after three seconds, and a command that reads the database, resolves
/// a guild's roles, and posts a message can exceed that on a slow connection. A
/// deferred interaction has fifteen minutes instead, so deferring first costs a
/// visible "thinking" indicator and removes a whole class of intermittent failure.
/// <br/>
/// Failures arrive as values rather than exceptions, so rendering one is a
/// reply rather than a catch. The description on an <see cref="Error"/> is
/// written to be read by the person who ran the command, so it is shown verbatim.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
public abstract class FushiModuleBase(IDispatcher dispatcher)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>
    /// Gets the guild the interaction came from.
    /// </summary>
    /// <remarks>
    /// Safe to read without a null check because every module is annotated to be
    /// available in guilds only, so Discord never routes a direct message here.
    /// </remarks>
    protected ulong GuildId => Context.Guild.Id;

    /// <summary>
    /// Gets the person who used the command.
    /// </summary>
    protected ulong ActorId => Context.User.Id;

    /// <summary>
    /// Sends a request into the application layer.
    /// </summary>
    /// <typeparam name="TResponse">The response the request produces.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <returns>The handler's response.</returns>
    protected Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request) =>
        dispatcher.SendAsync(request);

    /// <summary>
    /// Dispatches a query and renders whichever of its two outcomes occurred.
    /// <br/>
    /// Named for dispatching rather than responding so that it does not hide the
    /// <c>RespondAsync</c> overloads this class inherits: a derived class that
    /// declares a member name stops overload resolution from reaching the base
    /// class's members of that name at all.
    /// </summary>
    /// <typeparam name="T">The value a successful response carries.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="render">Builds the view for a successful response.</param>
    /// <param name="ephemeral">Whether the reply is visible only to the caller.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    protected async Task DispatchAsync<T>(
        IRequest<Result<T>> request,
        Func<T, MessageComponent> render,
        bool ephemeral = true)
    {
        ArgumentNullException.ThrowIfNull(render);

        await DeferAsync(ephemeral);

        Result<T> result = await SendAsync(request);

        if (result.IsFailure)
        {
            await FailAsync(result.Error);
            return;
        }

        await SendViewAsync(render(result.Value), ephemeral);
    }

    /// <summary>
    /// Dispatches a command and reports whether it was carried out.
    /// </summary>
    /// <param name="request">The command to send.</param>
    /// <param name="title">What to say when it succeeded.</param>
    /// <param name="description">The detail to add, if any is worth stating.</param>
    /// <param name="ephemeral">Whether the reply is visible only to the caller.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    protected async Task DispatchAsync(
        IRequest<Result> request,
        string title,
        string? description = null,
        bool ephemeral = true)
    {
        await DeferAsync(ephemeral);

        Result result = await SendAsync(request);

        if (result.IsFailure)
        {
            await FailAsync(result.Error);
            return;
        }

        await SendViewAsync(Replies.Success(title, description), ephemeral);
    }

    /// <summary>
    /// Sends a view as a follow-up to an interaction already deferred.
    /// </summary>
    /// <remarks>
    /// The one place the components-v2 flag is set for command replies. A v2
    /// message may carry no content and no embeds, so every send has to opt in
    /// explicitly and Discord rejects the message outright if it does not — which
    /// makes funnelling all of them through here the difference between one
    /// correct call site and twenty that each have to remember.
    /// </remarks>
    /// <param name="view">The message to send.</param>
    /// <param name="ephemeral">Whether it is visible only to the caller.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    protected Task SendViewAsync(MessageComponent view, bool ephemeral = true) =>
        FollowupAsync(
            components: view,
            ephemeral: ephemeral,
            flags: MessageFlags.ComponentsV2);

    /// <summary>
    /// Reports a failure on an interaction that has already been deferred.
    /// </summary>
    /// <remarks>
    /// Always ephemeral, whatever the command's usual visibility. A refusal is
    /// addressed to the person who tried, and posting it publicly would turn a
    /// mistyped code into something the whole channel reads.
    /// </remarks>
    /// <param name="error">The failure to report.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    protected Task FailAsync(Error error) => SendViewAsync(Replies.Error(error));

    /// <summary>
    /// Reports a failure this layer detected before anything was dispatched, and
    /// so before the interaction was deferred.
    /// </summary>
    /// <param name="error">The failure to report.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    protected Task RefuseAsync(Error error) =>
        RespondAsync(
            components: Replies.Error(error),
            ephemeral: true,
            flags: MessageFlags.ComponentsV2);

    /// <summary>
    /// Asks for confirmation before carrying an action out.
    /// </summary>
    /// <param name="title">The action awaiting confirmation.</param>
    /// <param name="description">What the action will do, stated plainly.</param>
    /// <param name="confirmId">The custom identifier of the confirming button.</param>
    /// <param name="confirmLabel">The confirming button's label.</param>
    /// <param name="destructive">Whether the action cannot be undone.</param>
    /// <returns>A task that completes once the prompt has been sent.</returns>
    protected Task ConfirmAsync(
        string title,
        string description,
        string confirmId,
        string confirmLabel,
        bool destructive = false) =>
        RespondAsync(
            components: Replies.Confirm(title, description, confirmId, confirmLabel, destructive),
            ephemeral: true,
            flags: MessageFlags.ComponentsV2);
}
