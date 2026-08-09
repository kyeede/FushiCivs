using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Votes;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Errors;
using Fushi.Core.Results;
using Fushi.Interactions.Components;
using Fushi.Interactions.Formatting;
using Fushi.Interactions.Modals;

// The vote receipt and the comment modal both live here rather than beside the
// slash commands, because a button press and a command that do the same thing
// should not answer differently.

namespace Fushi.Interactions.Modules;

/// <summary>
/// The Approve, Reject, and Abstain buttons on a review message, and the comment
/// modal behind them.
/// </summary>
/// <remarks>
/// This is where nearly all voting actually happens. The buttons sit on the
/// submission's own message in the review channel, so voting is a single press
/// next to what is being voted on, with no code to type and nothing to look up.
/// <c>/vote cast</c> exists for when that message has scrolled out of reach.
/// <br/>
/// The message is public, so anybody in the channel can press a button. That is
/// not a hole: the right to vote is checked by the handler against the guild's
/// grants, and somebody without one gets the same refusal they would get from the
/// command. Hiding the buttons per-viewer is not something Discord offers, and
/// hiding them from everyone would cost the panel the feature.
/// <br/>
/// Every reply is ephemeral, because the aggregate tally is public but who voted
/// which way is not.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
[CommandContextType(InteractionContextType.Guild)]
public sealed class VoteComponentModule(IDispatcher dispatcher) : ComponentModuleBase(dispatcher)
{
    /// <summary>
    /// Records a vote pressed on a review message.
    /// </summary>
    /// <param name="choice">The choice encoded in the button.</param>
    /// <param name="code">The submission's short code.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:vote:*:*")]
    public async Task CastAsync(string choice, string code)
    {
        if (!TryParseChoice(choice, out VoteChoice parsed))
        {
            // Only reachable from a button this project did not build, or one
            // built by a version that spelled a choice differently.
            await RefuseAsync(UnknownChoice);
            return;
        }

        await DeferAsync(ephemeral: true);

        Result<VoteReceiptModel> result =
            await SendAsync(new CastVote(GuildId, ActorId, code, parsed, Comment: null));

        if (result.IsFailure)
        {
            await FailAsync(result.Error);
            return;
        }

        await SendViewAsync(VoteViews.Receipt(result.Value, offerComment: true));
    }

    /// <summary>
    /// Opens the modal that attaches a comment to a vote already recorded.
    /// </summary>
    /// <param name="choice">The choice the vote recorded.</param>
    /// <param name="code">The submission's short code.</param>
    /// <returns>A task that completes once the modal has been shown.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:votenote:*:*")]
    public Task CommentAsync(string choice, string code) =>
        Context.Interaction.RespondWithModalAsync<VoteCommentModal>(
            $"{ComponentIds.PREFIX}:m:votenote:{choice}:{code}");

    /// <summary>
    /// Stores the comment collected by the modal.
    /// </summary>
    /// <remarks>
    /// Sent as a fresh vote carrying the comment rather than as an edit of the
    /// stored one. The vote command already replaces an existing vote in place,
    /// so re-casting the same choice with a comment attached leaves the tally
    /// untouched and needs no second way of writing to a vote.
    /// </remarks>
    /// <param name="choice">The choice the vote recorded.</param>
    /// <param name="code">The submission's short code.</param>
    /// <param name="modal">The submitted modal.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [ModalInteraction($"{ComponentIds.PREFIX}:m:votenote:*:*")]
    public async Task CommentSubmittedAsync(string choice, string code, VoteCommentModal modal)
    {
        ArgumentNullException.ThrowIfNull(modal);

        if (!TryParseChoice(choice, out VoteChoice parsed))
        {
            await RefuseAsync(UnknownChoice);
            return;
        }

        await DeferAsync(ephemeral: true);

        Result<VoteReceiptModel> result =
            await SendAsync(new CastVote(GuildId, ActorId, code, parsed, modal.Comment));

        if (result.IsFailure)
        {
            await FailAsync(result.Error);
            return;
        }

        await SendViewAsync(Replies.Success(
            "Comment saved",
            $"It is attached to your **{Display.Of(parsed)}** vote on `{result.Value.Code}`."));
    }

    private static bool TryParseChoice(string value, out VoteChoice choice)
    {
        switch (value)
        {
            case "approve":
                choice = VoteChoice.Approve;
                return true;
            case "reject":
                choice = VoteChoice.Reject;
                return true;
            case "abstain":
                choice = VoteChoice.Abstain;
                return true;
            default:
                choice = default;
                return false;
        }
    }

    private static Error UnknownChoice => Error.Validation(
        "Interaction.UnknownChoice",
        "That button is from an older version of the bot and no longer works. "
        + "Use `/vote cast` instead.");
}
