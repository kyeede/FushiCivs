using Discord;
using Discord.WebSocket;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Core.Results;
using Fushi.Interactions.Formatting;

namespace Fushi.Interactions.Modules;

/// <summary>
/// The shared behaviour of modules that answer a button, a select menu, or a
/// modal rather than a slash command.
/// </summary>
/// <remarks>
/// A component interaction differs from a command in where its reply belongs.
/// A command has no message yet, so it creates one; a component already has the
/// message it was pressed on, and the useful thing to do is nearly always to
/// change that message in place — a confirmation prompt becomes the outcome, a
/// page of a list becomes the next page. Editing rather than appending is what
/// keeps an ephemeral panel a single message instead of a growing column of them.
/// <br/>
/// So the pattern here is defer, then modify the original response, rather than
/// the command layer's defer-then-follow-up. Deferring first for the same reason:
/// three seconds is not a lot when a database and Discord are both involved.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
public abstract class ComponentModuleBase(IDispatcher dispatcher) : FushiModuleBase(dispatcher)
{
    /// <summary>
    /// Gets the component interaction being handled.
    /// </summary>
    /// <remarks>
    /// A cast rather than a typed context, because these modules and the command
    /// modules share one interaction context type. Safe because Discord.Net only
    /// routes a component interaction to a method annotated to receive one.
    /// </remarks>
    protected SocketMessageComponent Component => (SocketMessageComponent)Context.Interaction;

    /// <summary>
    /// Replaces the message the component sits on.
    /// </summary>
    /// <remarks>
    /// There is no longer a variant that keeps a separate set of controls. Under
    /// components v2 a view already contains its own buttons, so replacing the
    /// view replaces them with it — the two could not be edited independently
    /// even if it were useful to.
    /// <br/>
    /// The flag has to be restated on every edit: Discord treats a message
    /// without it as a classic message and rejects a v2 payload sent that way.
    /// </remarks>
    /// <param name="view">What the message should now be.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    protected Task ReplaceAsync(MessageComponent view) =>
        ModifyOriginalResponseAsync(message =>
        {
            message.Components = view;
            message.Flags = MessageFlags.ComponentsV2;
        });

    /// <summary>
    /// Carries out a confirmed command and reports the outcome in place of the
    /// prompt.
    /// </summary>
    /// <param name="request">The command to send.</param>
    /// <param name="title">What to say when it succeeded.</param>
    /// <param name="description">The detail to add, if any is worth stating.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    protected async Task ConfirmedAsync(
        IRequest<Result> request,
        string title,
        string? description = null)
    {
        await DeferAsync();

        Result result = await SendAsync(request);

        await ReplaceAsync(result.IsFailure
            ? Replies.Error(result.Error)
            : Replies.Success(title, description));
    }

    /// <summary>
    /// Carries out a confirmed command that produces a value, and renders it in
    /// place of the prompt.
    /// </summary>
    /// <typeparam name="T">The value a successful response carries.</typeparam>
    /// <param name="request">The command to send.</param>
    /// <param name="render">Builds the view for a successful response.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    protected async Task ConfirmedAsync<T>(
        IRequest<Result<T>> request,
        Func<T, MessageComponent> render)
    {
        ArgumentNullException.ThrowIfNull(render);

        await DeferAsync();

        Result<T> result = await SendAsync(request);

        await ReplaceAsync(result.IsFailure ? Replies.Error(result.Error) : render(result.Value));
    }
}
