using System.Globalization;
using System.Text;

using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Permissions;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Errors;
using Fushi.Core.Results;
using Fushi.Core.Utilities;
using Fushi.Core.Utilities.Paging;
using Fushi.Interactions.Components;
using Fushi.Interactions.Formatting;
using Fushi.Interactions.Modals;

namespace Fushi.Interactions.Modules;

/// <summary>
/// The menus and buttons voting rights are handed out and taken back through.
/// </summary>
/// <remarks>
/// The selection arrives as <see cref="IMentionable"/>, which is what a mentionable
/// menu returns and what makes the panel worth having: a user and a role come back
/// through one control, and which of the two it is can be read off the type rather
/// than inferred from which of two options somebody filled in.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
[CommandContextType(InteractionContextType.Guild)]
public sealed class VoterComponentModule(IDispatcher dispatcher) : ComponentModuleBase(dispatcher)
{
    /// <summary>
    /// Returns to the panel that hands out voting rights.
    /// </summary>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.VOTER_GRANT)]
    public async Task GrantAsync()
    {
        await DeferAsync();
        await ReplaceAsync(VoterPanels.Grant());
    }

    /// <summary>
    /// Grants everyone picked the right to vote.
    /// </summary>
    /// <remarks>
    /// Applied one at a time and reported as a whole. A partial failure is
    /// possible — a guild can reject one target and accept another — so the reply
    /// names what succeeded rather than claiming the selection went through.
    /// </remarks>
    /// <param name="targets">The users and roles picked.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.VOTER_GRANT_PICK)]
    public async Task GrantPickedAsync(IMentionable[] targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Length == 0)
        {
            await DeferAsync();
            await ReplaceAsync(VoterPanels.Grant());
            return;
        }

        await DeferAsync();

        StringBuilder granted = new(targets.Length * 40);
        Error? refusal = null;
        int count = 0;

        foreach (IMentionable target in targets)
        {
            (VotingPermissionScope scope, ulong id) = Identify(target);

            Result result = await SendAsync(
                new GrantVotingPermission(GuildId, ActorId, scope, id));

            if (result.IsFailure)
            {
                refusal ??= result.Error;
                continue;
            }

            if (count > 0)
            {
                _ = granted.Append(", ");
            }

            _ = granted.Append(Mention(scope, id));
            count++;
        }

        if (count == 0)
        {
            await ReplaceAsync(Replies.Error(refusal ?? NothingGranted));
            return;
        }

        string summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{granted} may now vote. A role covers everyone who holds it.");

        // The note is offered only when there is exactly one grant to attach it
        // to, since one note spread across several would be a guess about which
        // of them it described.
        (VotingPermissionScope Scope, ulong TargetId)? single = count == 1 && targets.Length == 1
            ? Identify(targets[0])
            : null;

        await ReplaceAsync(VoterPanels.Granted(summary, single));
    }

    /// <summary>
    /// Takes back the right to vote from whoever was picked.
    /// </summary>
    /// <remarks>
    /// A role goes through a confirmation first, because one grant can be the
    /// reason a great many people can vote and the menu gives no indication of how
    /// many. A single user does not, since the blast radius is the person named.
    /// </remarks>
    /// <param name="targets">The user or role picked.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.VOTER_REVOKE_PICK)]
    public async Task RevokePickedAsync(IMentionable[] targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        await DeferAsync();

        if (targets.Length == 0)
        {
            await ReplaceAsync(VoterPanels.Revoke());
            return;
        }

        (VotingPermissionScope scope, ulong id) = Identify(targets[0]);

        if (scope == VotingPermissionScope.Role)
        {
            await ReplaceAsync(Replies.Confirm(
                "Revoke this role's voting rights?",
                $"Everyone who can vote only because they have {MentionUtility.Role(id)} will "
                + "stop being able to. Anyone holding a separate grant of their own keeps it.",
                ComponentIds.ConfirmRevoke(scope, id),
                "Revoke",
                destructive: true));

            return;
        }

        Result result = await SendAsync(new RevokeVotingPermission(GuildId, ActorId, scope, id));

        await ReplaceAsync(result.IsFailure
            ? Replies.Error(result.Error)
            : VoterPanels.Revoke($"{MentionUtility.User(id)} may no longer vote."));
    }

    /// <summary>
    /// Shows who may vote, so the panels and the list are one surface.
    /// </summary>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.VOTER_LIST)]
    public async Task ListAsync()
    {
        await DeferAsync();

        Result<Page<VotingPermissionModel>> result =
            await SendAsync(new ListVotingPermissions(GuildId, PageRequest.Clamp(1)));

        await ReplaceAsync(result.IsFailure
            ? Replies.Error(result.Error)
            : GuildViews.Permissions(
                result.Value,
                Pager.Navigation(result.Value.Info, n => ComponentIds.Page("voter", n))));
    }

    /// <summary>
    /// Opens the dialogue that records why a grant exists.
    /// </summary>
    /// <param name="scope">Whether the grant covers a user or a role.</param>
    /// <param name="targetId">Who or what the grant covers.</param>
    /// <returns>A task that completes once the dialogue has been shown.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:vtr:note:*:*")]
    public Task NoteAsync(string scope, string targetId) =>
        Context.Interaction.RespondWithModalAsync<GrantNoteModal>(
            $"{ComponentIds.PREFIX}:m:vtrnote:{scope}:{targetId}");

    /// <summary>
    /// Stores the note the dialogue collected.
    /// </summary>
    /// <remarks>
    /// Sent as a fresh grant carrying the note rather than as an edit. Granting
    /// somebody who already has rights updates the existing row, so re-granting
    /// with a note attached changes nothing except the note.
    /// </remarks>
    /// <param name="scope">Whether the grant covers a user or a role.</param>
    /// <param name="targetId">Who or what the grant covers.</param>
    /// <param name="modal">The submitted dialogue.</param>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [ModalInteraction($"{ComponentIds.PREFIX}:m:vtrnote:*:*")]
    public async Task NoteSubmittedAsync(string scope, string targetId, GrantNoteModal modal)
    {
        ArgumentNullException.ThrowIfNull(modal);

        if (!TryParseTarget(scope, targetId, out VotingPermissionScope parsed, out ulong id))
        {
            await RefuseAsync(UnknownTarget);
            return;
        }

        await DeferAsync(ephemeral: true);

        Result result = await SendAsync(
            new GrantVotingPermission(GuildId, ActorId, parsed, id, modal.Note));

        await SendViewAsync(result.IsFailure
            ? Replies.Error(result.Error)
            : Replies.Success("Note saved", $"It is shown beside {Mention(parsed, id)} in `/voter list`."));
    }

    /// <summary>
    /// Reads what kind of thing a mentionable selection is.
    /// </summary>
    /// <remarks>
    /// A mentionable menu can also return a channel in principle. It cannot here,
    /// because the menu is built without channel types, so anything that is not a
    /// role is a user.
    /// </remarks>
    /// <param name="target">The selection to identify.</param>
    /// <returns>The scope a grant would use, and the snowflake it covers.</returns>
    private static (VotingPermissionScope Scope, ulong Id) Identify(IMentionable target) =>
        target is IRole role
            ? (VotingPermissionScope.Role, role.Id)
            : (VotingPermissionScope.User, ((IUser)target).Id);

    private static string Mention(VotingPermissionScope scope, ulong id) =>
        scope == VotingPermissionScope.Role
            ? MentionUtility.Role(id)
            : MentionUtility.User(id);

    private static bool TryParseTarget(
        string scope,
        string targetId,
        out VotingPermissionScope parsed,
        out ulong id)
    {
        parsed = scope == "role" ? VotingPermissionScope.Role : VotingPermissionScope.User;

        return ulong.TryParse(targetId, CultureInfo.InvariantCulture, out id)
            && (scope is "role" or "user");
    }

    private static Error NothingGranted => Error.Validation(
        "Interaction.NothingGranted",
        "Nothing was granted. Everyone picked either already has the right to vote or could "
        + "not be granted it.");

    private static Error UnknownTarget => Error.Validation(
        "Interaction.UnknownTarget",
        "That button is from an older version of the bot. Grant the right again from "
        + "`/voter grant` to attach a note.");
}
