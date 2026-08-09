using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Votes;
using Fushi.Core.Entities.Submissions;
using Fushi.Interactions.Autocomplete;
using Fushi.Interactions.Formatting;

namespace Fushi.Interactions.Modules;

/// <summary>
/// <c>/vote</c> — recording and withdrawing a vote.
/// </summary>
/// <remarks>
/// No Discord permission gates this group and none grants it. The right to vote
/// comes from a grant recorded by <c>/voter grant</c> and nothing else, so the
/// commands are visible to everyone and refused by the handler for anyone without
/// one. Showing them to everybody is deliberate: "why can I not vote" is a
/// question with an answer, and a command that is simply absent does not prompt
/// anyone to ask it.
/// <br/>
/// Every reply here is ephemeral without exception. A visible confirmation would
/// disclose how somebody voted to the whole channel, and a panel that watches
/// each other vote in real time is a panel that anchors on whoever votes first.
/// The aggregate tally is public; the individual votes behind it are not.
/// <br/>
/// The buttons on the review message are the path nearly everyone takes. These
/// commands are the fallback for when that message has scrolled away.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
[Group("vote", "Cast or retract your vote on an application.")]
[CommandContextType(InteractionContextType.Guild)]
public sealed class VoteModule(IDispatcher dispatcher) : FushiModuleBase(dispatcher)
{
    /// <summary>
    /// Records a vote, replacing any the caller had already cast.
    /// </summary>
    /// <param name="code">The submission's short code.</param>
    /// <param name="choice">How the caller is voting.</param>
    /// <param name="comment">An optional justification, stored with the vote.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("cast", "Vote on an application.")]
    public async Task CastAsync(
        [Summary("code", "The application's code, or part of its title.")]
        [Autocomplete(typeof(SubmissionCodeAutocompleteHandler))] string code,
        [Summary("choice", "How you are voting.")] VoteChoice choice,
        [Summary("comment", "Why, if you want it on the record.")]
        [MaxLength(512)] string? comment = null)
    {
        await DeferAsync(ephemeral: true);

        Core.Results.Result<VoteReceiptModel> result =
            await SendAsync(new CastVote(GuildId, ActorId, code, choice, comment));

        if (result.IsFailure)
        {
            await FailAsync(result.Error);
            return;
        }

        // The comment button is offered only when no comment was given, so the
        // reply after an explained vote is not cluttered by an invitation to
        // explain it again.
        await SendViewAsync(VoteViews.Receipt(result.Value, offerComment: comment is null));
    }

    /// <summary>
    /// Removes the caller's vote.
    /// </summary>
    /// <remarks>
    /// Needs no voting grant. Somebody whose grant was revoked after they voted
    /// should still be able to take their vote back, and requiring the right they
    /// no longer have would trap it on the record.
    /// </remarks>
    /// <param name="code">The submission's short code.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("retract", "Remove your vote from an application.")]
    public Task RetractAsync(
        [Summary("code", "The application's code, or part of its title.")]
        [Autocomplete(typeof(SubmissionCodeAutocompleteHandler))] string code) =>
        DispatchAsync(
            new RetractVote(GuildId, ActorId, code),
            "Vote retracted",
            "It no longer counts towards the tally.");
}
