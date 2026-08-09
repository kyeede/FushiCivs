using System.Globalization;

using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;

namespace Fushi.Interactions.Components;

/// <summary>
/// The custom identifiers that route a button, select menu, or modal back to the
/// method that handles it.
/// </summary>
/// <remarks>
/// Discord hands a component's custom identifier back verbatim when someone uses
/// it, and Discord.Net matches that string against the patterns on
/// <c>ComponentInteractionAttribute</c>, turning each <c>*</c> into an argument.
/// The identifier is therefore the only state a component carries: everything a
/// handler needs to act has to be encoded here, or looked up again from the
/// database.
/// <br/>
/// That is a deliberate choice over holding pending interactions in memory. A
/// button pressed an hour after it was posted, or after a restart, still works,
/// because there was never a server-side entry to expire. The cost is a 100
/// character budget, which a six-character short code and a snowflake fit inside
/// comfortably.
/// <br/>
/// Every identifier starts with <see cref="PREFIX"/> so that a component posted
/// by another bot, or by an older version of this one, is never mistaken for a
/// current route.
/// </remarks>
internal static class ComponentIds
{
    /// <summary>
    /// The leading segment every identifier this project produces begins with.
    /// </summary>
    public const string PREFIX = "fushi";

    /// <summary>
    /// The character separating segments of an identifier.
    /// </summary>
    /// <remarks>
    /// Safe as a separator because the only variable segments are short codes,
    /// which are Crockford Base32, and snowflakes, which are decimal digits.
    /// Neither can contain a colon.
    /// </remarks>
    public const char SEPARATOR = ':';

    /// <summary>
    /// The identifier of the button that dismisses an ephemeral prompt.
    /// </summary>
    public const string DISMISS = $"{PREFIX}:x";

    /// <summary>
    /// Builds the identifier for a vote button on a submission's review message.
    /// </summary>
    /// <param name="choice">The choice the button records.</param>
    /// <param name="code">The submission's short code.</param>
    /// <returns>The custom identifier.</returns>
    public static string Vote(VoteChoice choice, string code) => Join("vote", Segment(choice), code);

    /// <summary>
    /// Names a choice as it appears inside an identifier.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than lower-cased from the enum name, so that renaming a
    /// member is a compile error here instead of a button that silently stops
    /// routing. Identifiers already posted to Discord outlive the code that made
    /// them, and these names are part of that wire format.
    /// </remarks>
    /// <param name="choice">The choice to name.</param>
    /// <returns>The identifier segment.</returns>
    public static string Segment(VoteChoice choice) => choice switch
    {
        VoteChoice.Approve => "approve",
        VoteChoice.Reject => "reject",
        VoteChoice.Abstain => "abstain",
        _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, "Unknown vote choice."),
    };

    /// <summary>
    /// Names a grant's scope as it appears inside an identifier.
    /// </summary>
    /// <param name="scope">The scope to name.</param>
    /// <returns>The identifier segment.</returns>
    public static string Segment(VotingPermissionScope scope) => scope switch
    {
        VotingPermissionScope.User => "user",
        VotingPermissionScope.Role => "role",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown grant scope."),
    };

    /// <summary>
    /// Builds the identifier for the button that attaches a comment to a vote
    /// already recorded.
    /// </summary>
    /// <remarks>
    /// Carries the choice as well as the code. Attaching a comment re-records the
    /// same vote with the comment on it, so the choice has to be known — and
    /// there is no query that reports how a given person voted, deliberately.
    /// The button is only ever shown on the receipt for a vote just cast, so the
    /// choice is known at the moment it is built.
    /// </remarks>
    /// <param name="choice">The choice the vote recorded.</param>
    /// <param name="code">The submission's short code.</param>
    /// <returns>The custom identifier.</returns>
    public static string VoteComment(VoteChoice choice, string code) =>
        Join("votenote", Segment(choice), code);

    /// <summary>
    /// Builds the identifier for the button that reposts an ephemeral submission
    /// where everyone can see it.
    /// </summary>
    /// <param name="code">The submission's short code.</param>
    /// <returns>The custom identifier.</returns>
    public static string Publish(string code) => Join("pub", code);

    /// <summary>
    /// Builds the identifier for the button that opens one row of a list in full.
    /// </summary>
    /// <remarks>
    /// Components v2 lets a row of a list carry its own button, so a list is
    /// navigable rather than a set of codes to be retyped into
    /// <c>/submission view</c>. The code is all the button needs: the detail is
    /// queried when it is pressed.
    /// </remarks>
    /// <param name="code">The submission's short code.</param>
    /// <returns>The custom identifier.</returns>
    public static string Open(string code) => Join("open", code);

    /// <summary>
    /// Builds the identifier for the button that revokes a grant from the row
    /// showing it.
    /// </summary>
    /// <param name="scope">Whether the grant covers a user or a role.</param>
    /// <param name="targetId">The user or role the grant covers.</param>
    /// <returns>The custom identifier.</returns>
    public static string Revoke(VotingPermissionScope scope, ulong targetId) =>
        Join("rev", Segment(scope), targetId.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Builds the identifier for a page button on a paginated list.
    /// </summary>
    /// <param name="view">The list being paged, such as <c>sub</c> or <c>cyc</c>.</param>
    /// <param name="page">The one-based page the button navigates to.</param>
    /// <returns>The custom identifier.</returns>
    public static string Page(string view, int page) =>
        Join("page", view, page.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Builds the identifier for a page button on the submission list, which
    /// carries its status filter as well as its page.
    /// </summary>
    /// <param name="status">The status filter, or <see langword="null"/> for all.</param>
    /// <param name="page">The one-based page the button navigates to.</param>
    /// <returns>The custom identifier.</returns>
    public static string SubmissionPage(SubmissionStatus? status, int page) =>
        Join(
            "page",
            "sub",
            status is null ? "-" : ((int)status.Value).ToString(CultureInfo.InvariantCulture),
            page.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Builds the identifier for the confirming button of a two-step action.
    /// </summary>
    /// <param name="action">The action being confirmed.</param>
    /// <param name="argument">
    /// The action's subject, such as a short code. Omitted for actions that have
    /// no subject.
    /// </param>
    /// <returns>The custom identifier.</returns>
    public static string Confirm(string action, string? argument = null) =>
        argument is null ? Join("ok", action) : Join("ok", action, argument);

    /// <summary>
    /// Builds the identifier for the confirming button of a grant revocation.
    /// </summary>
    /// <param name="scope">Whether the grant covers a user or a role.</param>
    /// <param name="targetId">The user or role the grant covers.</param>
    /// <returns>The custom identifier.</returns>
    public static string ConfirmRevoke(VotingPermissionScope scope, ulong targetId) =>
        Join("ok", "revoke", Segment(scope), targetId.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Names a channel's role as it appears inside an identifier.
    /// </summary>
    /// <param name="role">The role to name.</param>
    /// <returns>The identifier segment.</returns>
    public static string Segment(GuildChannelRole role) => role switch
    {
        GuildChannelRole.Intake => "intake",
        GuildChannelRole.Review => "review",
        GuildChannelRole.Results => "results",
        GuildChannelRole.Archive => "archive",
        GuildChannelRole.Log => "log",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown channel role."),
    };

    /// <summary>
    /// The identifier of the button that returns to the configuration overview.
    /// </summary>
    public const string CONFIG_HOME = $"{PREFIX}:cfg:home";

    /// <summary>
    /// The identifier of the button that opens the channel routing panel.
    /// </summary>
    public const string CONFIG_CHANNELS = $"{PREFIX}:cfg:chan";

    /// <summary>
    /// The identifier of the button that opens the voting policy panel.
    /// </summary>
    public const string CONFIG_POLICY = $"{PREFIX}:cfg:pol";

    /// <summary>
    /// The identifier of the button that opens the schedule panel.
    /// </summary>
    public const string CONFIG_SCHEDULE = $"{PREFIX}:cfg:sch";

    /// <summary>
    /// Builds the identifier for the button that opens one channel's picker.
    /// </summary>
    /// <param name="role">The role being configured.</param>
    /// <returns>The custom identifier.</returns>
    public static string ChannelOpen(GuildChannelRole role) =>
        Join("cfg", "chan", "open", Segment(role));

    /// <summary>
    /// Builds the identifier for the channel select on one channel's picker.
    /// </summary>
    /// <param name="role">The role being configured.</param>
    /// <returns>The custom identifier.</returns>
    public static string ChannelPick(GuildChannelRole role) =>
        Join("cfg", "chan", "set", Segment(role));

    /// <summary>
    /// Builds the identifier for the button that unassigns a channel.
    /// </summary>
    /// <param name="role">The role being cleared.</param>
    /// <returns>The custom identifier.</returns>
    public static string ChannelClear(GuildChannelRole role) =>
        Join("cfg", "chan", "clr", Segment(role));

    /// <summary>
    /// The identifier of the approval threshold select on the policy panel.
    /// </summary>
    public const string POLICY_RATIO = $"{PREFIX}:cfg:pol:ratio";

    /// <summary>
    /// The identifier of the quorum select on the policy panel.
    /// </summary>
    public const string POLICY_QUORUM = $"{PREFIX}:cfg:pol:quorum";

    /// <summary>
    /// Builds the identifier for a button that flips one of the voting switches.
    /// </summary>
    /// <remarks>
    /// The value the switch is being moved to is encoded alongside the switch's
    /// name, rather than left for the handler to work out by reading the current
    /// setting and inverting it. A button that says "Allow" should allow even if
    /// somebody else changed the setting in the meantime — reading the panel and
    /// pressing what it offers should do what the label said, not the opposite.
    /// </remarks>
    /// <param name="rule">The switch's name, such as <c>abstain</c>.</param>
    /// <param name="allow">The value to set it to.</param>
    /// <returns>The custom identifier.</returns>
    public static string PolicyToggle(string rule, bool allow) =>
        Join("cfg", "pol", "tog", rule, allow ? "1" : "0");

    /// <summary>
    /// The identifier of the day-of-week select menu on the schedule panel.
    /// </summary>
    public const string DAY_SELECT = $"{PREFIX}:cfg:days";

    /// <summary>
    /// Builds the identifier for a button that applies a preset set of cycle days.
    /// </summary>
    /// <param name="preset">The preset's name.</param>
    /// <returns>The custom identifier.</returns>
    public static string DayPreset(string preset) => Join("cfg", "preset", preset);

    /// <summary>
    /// Builds the identifier for the button that opens one end of the voting
    /// window for editing.
    /// </summary>
    /// <param name="edge">Which end, as <c>open</c> or <c>close</c>.</param>
    /// <returns>The custom identifier.</returns>
    public static string TimeOpen(string edge) => Join("cfg", "time", edge);

    /// <summary>
    /// Builds the identifier for the hour select on a time picker.
    /// </summary>
    /// <param name="edge">Which end of the window is being set.</param>
    /// <returns>The custom identifier.</returns>
    public static string TimeHour(string edge) => Join("cfg", "hour", edge);

    /// <summary>
    /// Builds the identifier for the minute select on a time picker.
    /// </summary>
    /// <param name="edge">Which end of the window is being set.</param>
    /// <returns>The custom identifier.</returns>
    public static string TimeMinute(string edge) => Join("cfg", "min", edge);

    /// <summary>
    /// The identifier of the button that opens the time zone picker.
    /// </summary>
    public const string ZONE_OPEN = $"{PREFIX}:cfg:tz";

    /// <summary>
    /// The identifier of the region select on the time zone picker.
    /// </summary>
    public const string ZONE_REGION = $"{PREFIX}:cfg:tzr";

    /// <summary>
    /// Builds the identifier for the zone select within a chosen region.
    /// </summary>
    /// <remarks>
    /// Carries the region and the page because a select menu holds at most
    /// twenty-five options and several regions have many times that. The
    /// alternative — remembering which region somebody was browsing — would be
    /// state that has to expire, for a panel that otherwise needs none.
    /// </remarks>
    /// <param name="region">The region being browsed, such as <c>Europe</c>.</param>
    /// <param name="page">The zero-based page within that region.</param>
    /// <returns>The custom identifier.</returns>
    public static string ZonePick(string region, int page) =>
        Join("cfg", "tzz", region, page.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Builds the identifier for a paging button on the time zone picker.
    /// </summary>
    /// <param name="region">The region being browsed.</param>
    /// <param name="page">The zero-based page to move to.</param>
    /// <returns>The custom identifier.</returns>
    public static string ZonePage(string region, int page) =>
        Join("cfg", "tzp", region, page.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The identifier of the button that opens the panel granting voting rights.
    /// </summary>
    public const string VOTER_GRANT = $"{PREFIX}:vtr:grant";

    /// <summary>
    /// The identifier of the select that picks who to grant voting rights to.
    /// </summary>
    public const string VOTER_GRANT_PICK = $"{PREFIX}:vtr:grant:pick";

    /// <summary>
    /// The identifier of the select that picks whose voting rights to remove.
    /// </summary>
    public const string VOTER_REVOKE_PICK = $"{PREFIX}:vtr:revoke:pick";

    /// <summary>
    /// The identifier of the button that lists everyone who may vote.
    /// </summary>
    public const string VOTER_LIST = $"{PREFIX}:vtr:list";

    /// <summary>
    /// Builds the identifier for the button that attaches a note to a grant.
    /// </summary>
    /// <remarks>
    /// Doubles as the identifier of the modal it opens. A modal carries no state
    /// of its own beyond what its identifier says, so reusing the one already
    /// naming the grant is what lets the note reach the right row when it is
    /// submitted.
    /// </remarks>
    /// <param name="scope">Whether the grant covers a user or a role.</param>
    /// <param name="targetId">Who or what the grant covers.</param>
    /// <returns>The custom identifier.</returns>
    public static string GrantNote(VotingPermissionScope scope, ulong targetId) =>
        Join("vtr", "note", Segment(scope), targetId.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Builds the identifier for a button that asks to carry out a cycle action.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Confirm"/>, which is the button inside the prompt.
    /// This one opens the prompt, so that a control offered on a status panel goes
    /// through the same confirmation as the command that does the same thing.
    /// </remarks>
    /// <param name="action">The action being asked for, such as <c>close</c>.</param>
    /// <param name="argument">The cycle it applies to, where it needs one.</param>
    /// <returns>The custom identifier.</returns>
    public static string CycleAction(string action, string? argument = null) =>
        argument is null ? Join("cyc", action) : Join("cyc", action, argument);

    /// <summary>
    /// Builds the identifier for a modal and the field inside it.
    /// </summary>
    /// <param name="name">The modal's name, such as <c>withdraw</c>.</param>
    /// <param name="argument">The subject the modal acts on.</param>
    /// <returns>The custom identifier.</returns>
    public static string Modal(string name, string argument) => Join("m", name, argument);

    private static string Join(params ReadOnlySpan<string> segments) =>
        string.Join(SEPARATOR, [PREFIX, .. segments]);
}
