using System.Globalization;

using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Cycles;
using Fushi.Application.Features.Guilds;
using Fushi.Application.Features.Permissions;
using Fushi.Application.Features.Submissions;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Errors;
using Fushi.Core.Identifiers;
using Fushi.Core.Results;
using Fushi.Core.Utilities;
using Fushi.Interactions.Components;
using Fushi.Interactions.Formatting;
using Fushi.Interactions.Modals;

namespace Fushi.Interactions.Modules;

/// <summary>
/// The confirming half of every two-step action, and the modals that collect a
/// reason on the way through.
/// </summary>
/// <remarks>
/// Each prompt is an ephemeral message, so only the person who ran the command
/// can see or press these buttons. That is what makes it safe to carry the
/// action's subject in the identifier without also checking who is pressing:
/// nobody else was ever shown the button. The Discord permission that gated the
/// original command is checked again by the handler, which is where it belongs.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
[CommandContextType(InteractionContextType.Guild)]
public sealed class ConfirmComponentModule(IDispatcher dispatcher) : ComponentModuleBase(dispatcher)
{
    /// <summary>
    /// Stops new cycles opening.
    /// </summary>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:ok:disable")]
    public Task DisableAsync() =>
        ConfirmedAsync(
            new SetGuildEnabled(GuildId, ActorId, Enabled: false),
            "Disabled",
            "No new cycle will open. Everything already configured is kept, and "
            + "`/config enable` puts it back.");

    /// <summary>
    /// Opens a cycle immediately.
    /// </summary>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:ok:cycle-open")]
    public async Task OpenCycleAsync()
    {
        await DeferAsync();

        Result<ShortCode> result = await SendAsync(new OpenCycle(GuildId, ActorId, BypassSchedule: true));

        await ReplaceAsync(result.IsFailure
            ? Replies.Error(result.Error)
            : Replies.Success(
                $"Cycle {result.Value} is open",
                "The queued submissions have been posted to the review channel."));
    }

    /// <summary>
    /// Stops the open cycle accepting votes.
    /// </summary>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:ok:cycle-close")]
    public Task CloseCycleAsync() =>
        ConfirmedAsync(
            new CloseCycle(GuildId, ActorId),
            "Cycle closed",
            "No more votes are accepted. Run `/cycle finalise` to decide the submissions "
            + "and publish the results.");

    /// <summary>
    /// Decides every submission in a closed cycle.
    /// </summary>
    /// <param name="code">The cycle's short code.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:ok:cycle-final:*")]
    public async Task FinaliseCycleAsync(string code)
    {
        if (!ShortCode.TryParse(code, out ShortCode parsed))
        {
            await RefuseAsync(Codes.Malformed(code));
            return;
        }

        await ConfirmedAsync(new FinaliseCycle(GuildId, parsed, ActorId), CycleViews.Receipt);
    }

    /// <summary>
    /// Opens the modal collecting why a cycle is being cancelled.
    /// </summary>
    /// <param name="code">The cycle's short code.</param>
    /// <returns>A task that completes once the modal has been shown.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:ok:cycle-cancel:*")]
    public Task CancelCycleAsync(string code) =>
        Context.Interaction.RespondWithModalAsync<CancelCycleModal>(
            ComponentIds.Modal("cancel", code));

    /// <summary>
    /// Cancels a cycle once its reason has been given.
    /// </summary>
    /// <param name="code">The cycle's short code.</param>
    /// <param name="modal">The submitted modal.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [ModalInteraction($"{ComponentIds.PREFIX}:m:cancel:*")]
    public async Task CancelCycleSubmittedAsync(string code, CancelCycleModal modal)
    {
        ArgumentNullException.ThrowIfNull(modal);

        if (!ShortCode.TryParse(code, out ShortCode parsed))
        {
            await RefuseAsync(Codes.Malformed(code));
            return;
        }

        await DeferAsync(ephemeral: true);

        Result result = await SendAsync(new CancelCycle(GuildId, parsed, ActorId, modal.Reason));

        await SendViewAsync(result.IsFailure
            ? Replies.Error(result.Error)
            : Replies.Success(
                $"Cycle {parsed} cancelled",
                "Its submissions are back in the queue and the votes cast under it are gone."));
    }

    /// <summary>
    /// Removes a role's voting grant.
    /// </summary>
    /// <param name="scope">The grant's scope, as encoded in the button.</param>
    /// <param name="target">The user or role the grant covers.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:ok:revoke:*:*")]
    public async Task RevokeAsync(string scope, string target)
    {
        if (!TryParseScope(scope, out VotingPermissionScope parsedScope)
            || !ulong.TryParse(target, CultureInfo.InvariantCulture, out ulong targetId))
        {
            await RefuseAsync(UnreadableButton);
            return;
        }

        string mention = parsedScope == VotingPermissionScope.Role
            ? MentionUtility.Role(targetId)
            : MentionUtility.User(targetId);

        await ConfirmedAsync(
            new RevokeVotingPermission(GuildId, ActorId, parsedScope, targetId),
            "Voting revoked",
            $"{mention} no longer carries the right to vote.");
    }

    /// <summary>
    /// Withdraws a submission once its reason has been given.
    /// </summary>
    /// <param name="code">The submission's short code.</param>
    /// <param name="modal">The submitted modal.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [ModalInteraction($"{ComponentIds.PREFIX}:m:withdraw:*")]
    public async Task WithdrawSubmittedAsync(string code, WithdrawModal modal)
    {
        ArgumentNullException.ThrowIfNull(modal);

        await DeferAsync(ephemeral: true);

        Result result = await SendAsync(
            new WithdrawSubmission(GuildId, ActorId, code, modal.Reason));

        await SendViewAsync(result.IsFailure
            ? Replies.Error(result.Error)
            : Replies.Success(
                "Withdrawn",
                $"`{code}` has been withdrawn and will not be considered."));
    }

    private static bool TryParseScope(string value, out VotingPermissionScope scope)
    {
        switch (value)
        {
            case "user":
                scope = VotingPermissionScope.User;
                return true;
            case "role":
                scope = VotingPermissionScope.Role;
                return true;
            default:
                scope = default;
                return false;
        }
    }

    private static Error UnreadableButton => Error.Validation(
        "Interaction.UnreadableButton",
        "That button is from an older version of the bot and no longer works. "
        + "Run the command again.");
}
