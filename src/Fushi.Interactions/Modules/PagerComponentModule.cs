using System.Globalization;

using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Cycles;
using Fushi.Application.Features.Permissions;
using Fushi.Application.Features.Submissions;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Results;
using Fushi.Core.Utilities;
using Fushi.Core.Utilities.Paging;
using Fushi.Interactions.Components;
using Fushi.Interactions.Formatting;

namespace Fushi.Interactions.Modules;

/// <summary>
/// The buttons under a paginated list, and the two buttons that appear on their
/// own: dismiss, and post publicly.
/// </summary>
/// <remarks>
/// Every one of these re-runs the query for the page it wants rather than reading
/// from a stored result set. Paging is therefore always against what is in the
/// database now, and a button pressed after a restart still works. The cost is a
/// query per press, which is the right trade for a list somebody is reading a
/// page at a time.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
[CommandContextType(InteractionContextType.Guild)]
public sealed class PagerComponentModule(IDispatcher dispatcher) : ComponentModuleBase(dispatcher)
{
    /// <summary>
    /// Clears an ephemeral panel the reader is finished with.
    /// </summary>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.DISMISS)]
    public async Task DismissAsync()
    {
        await DeferAsync();
        await ReplaceAsync(Replies.Success("Dismissed"));
    }

    /// <summary>
    /// Moves to another page of the voting grants.
    /// </summary>
    /// <param name="page">The page to show.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:page:voter:*")]
    public async Task VoterPageAsync(string page)
    {
        await DeferAsync();

        int number = Number(page);

        Result<Page<VotingPermissionModel>> result =
            await SendAsync(new ListVotingPermissions(GuildId, PageRequest.Clamp(number)));

        if (result.IsFailure)
        {
            await ReplaceAsync(Replies.Error(result.Error));
            return;
        }

        await ReplaceAsync(GuildViews.Permissions(
            result.Value,
            Pager.Navigation(result.Value.Info, n => ComponentIds.Page("voter", n))));
    }

    /// <summary>
    /// Moves to another page of the cycle history.
    /// </summary>
    /// <param name="page">The page to show.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:page:cyc:*")]
    public async Task CyclePageAsync(string page)
    {
        await DeferAsync();

        Result<Page<CycleSummaryModel>> result =
            await SendAsync(new ListCycles(GuildId, PageRequest.Clamp(Number(page))));

        if (result.IsFailure)
        {
            await ReplaceAsync(Replies.Error(result.Error));
            return;
        }

        await ReplaceAsync(CycleViews.List(
            result.Value,
            Pager.Navigation(result.Value.Info, n => ComponentIds.Page("cyc", n))));
    }

    /// <summary>
    /// Moves to another page of the submission list, keeping its status filter.
    /// </summary>
    /// <param name="filter">
    /// The status the list is filtered to, or <c>-</c> when it is unfiltered.
    /// </param>
    /// <param name="page">The page to show.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:page:sub:*:*")]
    public async Task SubmissionPageAsync(string filter, string page)
    {
        await DeferAsync();

        SubmissionStatus? status = ParseStatus(filter);

        Result<Page<SubmissionSummaryModel>> result = await SendAsync(
            new ListSubmissions(GuildId, status, Number(page), PageRequest.DEFAULT_SIZE));

        if (result.IsFailure)
        {
            await ReplaceAsync(Replies.Error(result.Error));
            return;
        }

        string heading = status == SubmissionStatus.Queued
            ? "Waiting for the next cycle"
            : "Applications";

        await ReplaceAsync(SubmissionViews.List(
            result.Value,
            heading,
            Pager.Navigation(result.Value.Info, n => ComponentIds.SubmissionPage(status, n))));
    }

    /// <summary>
    /// Opens one row of a list in full, without disturbing the list.
    /// </summary>
    /// <remarks>
    /// A second ephemeral message rather than a replacement, so the reader keeps
    /// their place. Components v2 is what makes this button possible at all: a row
    /// of a list can now carry a control, where an embed could only offer the code
    /// to be retyped.
    /// </remarks>
    /// <param name="code">The submission's short code.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:open:*")]
    public async Task OpenAsync(string code)
    {
        await DeferAsync();

        Result<SubmissionDetailModel> result = await SendAsync(new GetSubmission(GuildId, code));

        await SendViewAsync(result.IsFailure
            ? Replies.Error(result.Error)
            : SubmissionViews.Detail(
                result.Value,
                Layout.Actions(
                    new ButtonBuilder(
                        "Post publicly",
                        ComponentIds.Publish(result.Value.Code),
                        ButtonStyle.Secondary),
                    new ButtonBuilder("Dismiss", ComponentIds.DISMISS, ButtonStyle.Secondary))));
    }

    /// <summary>
    /// Asks whether a grant shown in the list should be revoked.
    /// </summary>
    /// <remarks>
    /// Always confirmed, where <c>/voter revoke</c> confirms only for roles. The
    /// command names its target in the words the caller typed; a button on a row
    /// is one misplaced press away from a neighbouring row, so the prompt is what
    /// makes the target explicit before anything is removed.
    /// </remarks>
    /// <param name="scope">Whether the grant covers a user or a role.</param>
    /// <param name="target">The user or role the grant covers.</param>
    /// <returns>A task that completes once the prompt has been sent.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:rev:*:*")]
    public Task RevokeAsync(string scope, string target)
    {
        if (!ulong.TryParse(target, CultureInfo.InvariantCulture, out ulong targetId))
        {
            return RefuseAsync(Codes.Malformed(target));
        }

        bool isRole = scope == ComponentIds.Segment(VotingPermissionScope.Role);

        return ConfirmAsync(
            isRole ? "Revoke this role's voting rights?" : "Revoke this user's voting rights?",
            isRole
                ? $"Everyone who can vote only because they have {MentionUtility.Role(targetId)} "
                    + "will stop being able to. Anyone holding a separate grant of their own keeps it."
                : $"{MentionUtility.User(targetId)} will no longer be able to vote.",
            ComponentIds.ConfirmRevoke(
                isRole ? VotingPermissionScope.Role : VotingPermissionScope.User,
                targetId),
            "Revoke",
            destructive: true);
    }

    /// <summary>
    /// Reposts a submission the caller was reading privately so the channel can
    /// see it.
    /// </summary>
    /// <remarks>
    /// The ephemeral original is left in place. Replacing it would remove what the
    /// caller was reading in order to show it to everybody else, which is a
    /// surprising thing for a button labelled "post publicly" to do.
    /// </remarks>
    /// <param name="code">The submission's short code.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:pub:*")]
    public async Task PublishAsync(string code)
    {
        await DeferAsync();

        Result<SubmissionDetailModel> result = await SendAsync(new GetSubmission(GuildId, code));

        if (result.IsFailure)
        {
            await ReplaceAsync(Replies.Error(result.Error));
            return;
        }

        await SendViewAsync(SubmissionViews.Detail(result.Value), ephemeral: false);
    }

    // A page number that will not parse means a hand-edited identifier, and page
    // one is a better answer than an error for something nobody can have done by
    // accident.
    private static int Number(string page) =>
        int.TryParse(page, CultureInfo.InvariantCulture, out int number) ? number : 1;

    private static SubmissionStatus? ParseStatus(string filter) =>
        int.TryParse(filter, CultureInfo.InvariantCulture, out int value)
        && Enum.IsDefined((SubmissionStatus)value)
            ? (SubmissionStatus)value
            : null;
}
