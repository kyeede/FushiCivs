using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Permissions;
using Fushi.Core.Utilities.Paging;
using Fushi.Interactions.Components;
using Fushi.Interactions.Formatting;

namespace Fushi.Interactions.Modules;

/// <summary>
/// <c>/voter</c> — who is allowed to vote.
/// </summary>
/// <remarks>
/// Voting is deny-by-default and entirely separate from Discord's permission
/// system: an administrator cannot vote without a grant, because administering a
/// server and sitting on the review panel are different jobs. Grants are purely
/// additive — there is no deny rule, so revoking a grant is removing it rather
/// than overriding it.
/// <br/>
/// Granting and revoking open a panel rather than taking a target as an option.
/// A grant covers a user or a role but never both, which as a pair of optional
/// options meant the command had to be refused whenever somebody filled in both
/// or neither. Discord has one menu that selects users and roles together, so the
/// panel removes that failure rather than validating it — and can grant several
/// at once, which the option pair could not do at all.
/// <br/>
/// Gated on Manage Roles, which is the closest Discord permission to "decides who
/// sits on the panel". Replies are ephemeral because who may vote is not public
/// information.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
[Group("voter", "Grant and revoke the right to vote.")]
[DefaultMemberPermissions(GuildPermission.ManageRoles)]
[CommandContextType(InteractionContextType.Guild)]
public sealed class VoterModule(IDispatcher dispatcher) : FushiModuleBase(dispatcher)
{
    /// <summary>
    /// Opens the panel that hands out voting rights.
    /// </summary>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("grant", "Allow users or roles to vote.")]
    public Task GrantAsync() => RespondAsync(
        components: VoterPanels.Grant(),
        ephemeral: true,
        flags: MessageFlags.ComponentsV2);

    /// <summary>
    /// Opens the panel that takes voting rights back.
    /// </summary>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("revoke", "Remove a user's or a role's right to vote.")]
    public Task RevokeAsync() => RespondAsync(
        components: VoterPanels.Revoke(),
        ephemeral: true,
        flags: MessageFlags.ComponentsV2);

    /// <summary>
    /// Lists every grant in the guild.
    /// </summary>
    /// <param name="page">The page to show, counting from one.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("list", "List everyone who may vote.")]
    public async Task ListAsync(
        [Summary("page", "The page to show.")][MinValue(1)] int page = 1)
    {
        await DeferAsync(ephemeral: true);

        Core.Results.Result<Page<VotingPermissionModel>> result =
            await SendAsync(new ListVotingPermissions(GuildId, PageRequest.Clamp(page)));

        if (result.IsFailure)
        {
            await FailAsync(result.Error);
            return;
        }

        await SendViewAsync(GuildViews.Permissions(
            result.Value,
            Pager.Navigation(result.Value.Info, n => ComponentIds.Page("voter", n))));
    }
}
