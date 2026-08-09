using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Cycles;
using Fushi.Core.Identifiers;
using Fushi.Core.Results;
using Fushi.Core.Utilities.Paging;
using Fushi.Interactions.Autocomplete;
using Fushi.Interactions.Components;
using Fushi.Interactions.Formatting;

namespace Fushi.Interactions.Modules;

/// <summary>
/// <c>/cycle</c> — opening, closing, and deciding a round of voting.
/// </summary>
/// <remarks>
/// Every command here is also something the scheduler does on its own at the
/// configured times. They exist for the cases a schedule does not cover: a round
/// that needs to start early, one that has to be abandoned, or a finalisation
/// that has to happen while somebody watches.
/// <br/>
/// All four state changes are confirmed first, because each is visible to
/// everyone in the guild the moment it happens and none of them can be quietly
/// undone.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
[Group("cycle", "Open, close, and finalise rounds of voting.")]
[DefaultMemberPermissions(GuildPermission.ManageMessages)]
[CommandContextType(InteractionContextType.Guild)]
public sealed class CycleModule(IDispatcher dispatcher) : FushiModuleBase(dispatcher)
{
    /// <summary>
    /// Shows the state of the cycle currently open.
    /// </summary>
    /// <remarks>
    /// The only command in this group that replies publicly. The aggregate state
    /// of a round — how long is left, how many submissions have been voted on —
    /// is meant to be visible, and having to run the command yourself to see it
    /// would be friction for no gain.
    /// </remarks>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("status", "Show the cycle currently open.")]
    public Task StatusAsync() =>
        DispatchAsync(new GetCycleStatus(GuildId), CycleViews.Status, ephemeral: false);

    /// <summary>
    /// Opens a cycle ahead of its schedule.
    /// </summary>
    /// <returns>A task that completes once the prompt has been sent.</returns>
    [SlashCommand("open", "Open a cycle now, without waiting for the schedule.")]
    public Task OpenAsync() =>
        ConfirmAsync(
            "Open a cycle now?",
            "Every queued submission is taken into it, posted to the review channel, and "
            + "opened for voting immediately. The closing time still comes from the schedule.",
            ComponentIds.Confirm("cycle-open"),
            "Open");

    /// <summary>
    /// Stops the open cycle accepting votes.
    /// </summary>
    /// <returns>A task that completes once the prompt has been sent.</returns>
    [SlashCommand("close", "Stop the open cycle accepting votes, without deciding anything.")]
    public Task CloseAsync() =>
        ConfirmAsync(
            "Close the open cycle?",
            "Voting stops immediately. Nothing is decided — run `/cycle finalise` afterwards "
            + "to apply the policy and publish results.",
            ComponentIds.Confirm("cycle-close"),
            "Close");

    /// <summary>
    /// Applies the policy to a closed cycle and publishes its results.
    /// </summary>
    /// <param name="code">The cycle to finalise.</param>
    /// <returns>A task that completes once the prompt has been sent.</returns>
    [SlashCommand("finalise", "Decide every submission in a closed cycle and publish the results.")]
    public async Task FinaliseAsync(
        [Summary("code", "The cycle to finalise.")]
        [Autocomplete(typeof(CycleCodeAutocompleteHandler))] string code)
    {
        if (!ShortCode.TryParse(code, out ShortCode parsed))
        {
            await RefuseAsync(Codes.Malformed(code));
            return;
        }

        await ConfirmAsync(
            $"Finalise cycle {parsed}?",
            "Every attached submission is decided against the policy the cycle opened under, "
            + "and the results are posted publicly. The cycle must already be closed.",
            ComponentIds.Confirm("cycle-final", parsed.ToString()),
            "Finalise");
    }

    /// <summary>
    /// Abandons a cycle and discards the votes cast under it.
    /// </summary>
    /// <param name="code">The cycle to cancel.</param>
    /// <returns>A task that completes once the prompt has been sent.</returns>
    [SlashCommand("cancel", "Abandon a cycle, returning its submissions and clearing its votes.")]
    public async Task CancelAsync(
        [Summary("code", "The cycle to cancel.")]
        [Autocomplete(typeof(CycleCodeAutocompleteHandler))] string code)
    {
        if (!ShortCode.TryParse(code, out ShortCode parsed))
        {
            await RefuseAsync(Codes.Malformed(code));
            return;
        }

        await ConfirmAsync(
            $"Cancel cycle {parsed}?",
            "Every submission goes back to the queue and **the votes cast under this cycle are "
            + "cleared**. They were cast under a round that no longer counts, and carrying them "
            + "into the next one would let one person's decision apply twice. This cannot be undone.",
            ComponentIds.Confirm("cycle-cancel", parsed.ToString()),
            "Cancel the cycle",
            destructive: true);
    }

    /// <summary>
    /// Lists recent cycles, newest first.
    /// </summary>
    /// <param name="page">The page to show, counting from one.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("list", "List recent cycles, newest first.")]
    public async Task ListAsync(
        [Summary("page", "The page to show.")][MinValue(1)] int page = 1)
    {
        await DeferAsync(ephemeral: true);

        Result<Page<CycleSummaryModel>> result =
            await SendAsync(new ListCycles(GuildId, PageRequest.Clamp(page)));

        if (result.IsFailure)
        {
            await FailAsync(result.Error);
            return;
        }

        await SendViewAsync(CycleViews.List(
            result.Value,
            Pager.Navigation(result.Value.Info, n => ComponentIds.Page("cyc", n))));
    }
}
