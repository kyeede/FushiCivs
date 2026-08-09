using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Core.Identifiers;
using Fushi.Interactions.Components;
using Fushi.Interactions.Formatting;

namespace Fushi.Interactions.Modules;

/// <summary>
/// The action buttons on a row of <c>/cycle list</c>.
/// </summary>
/// <remarks>
/// These do not carry anything out. Each one asks the same question the matching
/// slash command asks and hands over to the same confirmation, so that closing a
/// cycle from a list and closing it from a command are the same act with the same
/// warning — rather than the button being the quiet shortcut that skips the part
/// explaining what is about to be discarded.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
[CommandContextType(InteractionContextType.Guild)]
public sealed class CycleComponentModule(IDispatcher dispatcher) : ComponentModuleBase(dispatcher)
{
    /// <summary>
    /// Asks whether the open cycle should stop accepting votes.
    /// </summary>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cyc:close")]
    public async Task CloseAsync()
    {
        await DeferAsync();
        await ReplaceAsync(Replies.Confirm(
            "Close the open cycle?",
            "Voting stops immediately. Nothing is decided — finalise it afterwards to apply "
            + "the policy and publish results.",
            ComponentIds.Confirm("cycle-close"),
            "Close"));
    }

    /// <summary>
    /// Asks whether a closed cycle should be decided.
    /// </summary>
    /// <param name="code">The cycle the button named.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cyc:final:*")]
    public async Task FinaliseAsync(string code)
    {
        await DeferAsync();

        if (!ShortCode.TryParse(code, out ShortCode parsed))
        {
            await ReplaceAsync(Replies.Error(Codes.Malformed(code)));
            return;
        }

        await ReplaceAsync(Replies.Confirm(
            $"Finalise cycle {parsed}?",
            "Every attached submission is decided against the policy the cycle opened under, "
            + "and the results are posted publicly. The cycle must already be closed.",
            ComponentIds.Confirm("cycle-final", parsed.ToString()),
            "Finalise"));
    }

    /// <summary>
    /// Asks whether a cycle should be abandoned.
    /// </summary>
    /// <param name="code">The cycle the button named.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cyc:cancel:*")]
    public async Task CancelAsync(string code)
    {
        await DeferAsync();

        if (!ShortCode.TryParse(code, out ShortCode parsed))
        {
            await ReplaceAsync(Replies.Error(Codes.Malformed(code)));
            return;
        }

        await ReplaceAsync(Replies.Confirm(
            $"Cancel cycle {parsed}?",
            "Every submission goes back to the queue and **the votes cast under this cycle are "
            + "cleared**. They were cast under a round that no longer counts, and carrying them "
            + "into the next one would let one person's decision apply twice. This cannot be undone.",
            ComponentIds.Confirm("cycle-cancel", parsed.ToString()),
            "Cancel the cycle",
            destructive: true));
    }
}
