using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Submissions;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Results;
using Fushi.Core.Utilities.Paging;
using Fushi.Interactions.Autocomplete;
using Fushi.Interactions.Components;
using Fushi.Interactions.Formatting;

namespace Fushi.Interactions.Modules;

/// <summary>
/// <c>/submission</c> — reading and withdrawing applications.
/// </summary>
/// <remarks>
/// Reads are open to anyone who can see the channel, so this group carries no
/// default permission. Withdrawal is restricted, but by the caller's relationship
/// to the record rather than by a Discord permission, which is a check only the
/// handler can make.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
[Group("submission", "Look up and manage applications.")]
[CommandContextType(InteractionContextType.Guild)]
public sealed class SubmissionModule(IDispatcher dispatcher) : FushiModuleBase(dispatcher)
{
    /// <summary>
    /// Shows one submission in full.
    /// </summary>
    /// <remarks>
    /// Ephemeral by default with a button to repost it publicly, rather than a
    /// <c>public</c> option decided before the reply is seen. Looking something up
    /// is usually private and occasionally worth sharing, and that is only known
    /// after reading it.
    /// </remarks>
    /// <param name="code">The submission's short code.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("view", "Show an application in full.")]
    public async Task ViewAsync(
        [Summary("code", "The application's code, or part of its title.")]
        [Autocomplete(typeof(SubmissionCodeAutocompleteHandler))] string code)
    {
        await DeferAsync(ephemeral: true);

        Result<SubmissionDetailModel> result = await SendAsync(new GetSubmission(GuildId, code));

        if (result.IsFailure)
        {
            await FailAsync(result.Error);
            return;
        }

        await SendViewAsync(SubmissionViews.Detail(
            result.Value,
            Layout.Actions(
                new ButtonBuilder(
                    "Post publicly",
                    ComponentIds.Publish(result.Value.Code),
                    ButtonStyle.Secondary),
                new ButtonBuilder("Dismiss", ComponentIds.DISMISS, ButtonStyle.Secondary))));
    }

    /// <summary>
    /// Lists submissions, optionally filtered by where they have reached.
    /// </summary>
    /// <param name="status">The status to filter to, or every status when omitted.</param>
    /// <param name="page">The page to show, counting from one.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("list", "List applications, newest first.")]
    public Task ListAsync(
        [Summary("status", "Show only applications at this stage.")] SubmissionStatus? status = null,
        [Summary("page", "The page to show.")][MinValue(1)] int page = 1) =>
        ShowListAsync(status, page, "Applications");

    /// <summary>
    /// Lists the submissions waiting for the next cycle.
    /// </summary>
    /// <remarks>
    /// A filtered view of the same list, given its own command because "what is
    /// waiting" is the question asked before opening a cycle, and asking it
    /// should not require knowing that "queued" is the status to filter on.
    /// </remarks>
    /// <param name="page">The page to show, counting from one.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("queue", "Show the applications waiting for the next cycle.")]
    public Task QueueAsync(
        [Summary("page", "The page to show.")][MinValue(1)] int page = 1) =>
        ShowListAsync(SubmissionStatus.Queued, page, "Waiting for the next cycle");

    /// <summary>
    /// Withdraws a submission from consideration.
    /// </summary>
    /// <remarks>
    /// Confirmed through a modal that also collects the reason, so the
    /// confirmation and the explanation are one step rather than two. Withdrawal
    /// is terminal: a withdrawn submission cannot be returned to the queue.
    /// </remarks>
    /// <param name="code">The submission's short code.</param>
    /// <returns>A task that completes once the modal has been shown.</returns>
    [SlashCommand("withdraw", "Withdraw an application from consideration.")]
    public async Task WithdrawAsync(
        [Summary("code", "The application's code, or part of its title.")]
        [Autocomplete(typeof(SubmissionCodeAutocompleteHandler))] string code)
    {
        if (!Core.Identifiers.ShortCode.TryParse(code, out Core.Identifiers.ShortCode parsed))
        {
            await RefuseAsync(Codes.Malformed(code));
            return;
        }

        await Context.Interaction.RespondWithModalAsync<Modals.WithdrawModal>(
            ComponentIds.Modal("withdraw", parsed.ToString()));
    }

    private async Task ShowListAsync(SubmissionStatus? status, int page, string heading)
    {
        await DeferAsync(ephemeral: true);

        Result<Page<SubmissionSummaryModel>> result =
            await SendAsync(new ListSubmissions(GuildId, status, page, PageRequest.DEFAULT_SIZE));

        if (result.IsFailure)
        {
            await FailAsync(result.Error);
            return;
        }

        await SendViewAsync(SubmissionViews.List(
            result.Value,
            heading,
            Pager.Navigation(result.Value.Info, n => ComponentIds.SubmissionPage(status, n))));
    }
}
