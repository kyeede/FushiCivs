using System.Globalization;

using Discord;

using Fushi.Application.Features.Guilds;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;
using Fushi.Interactions.Components;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// Builds the panels a guild is configured through.
/// </summary>
/// <remarks>
/// Configuration is done entirely with components. No <c>/config</c> command takes
/// a value: each one opens a panel, and every setting on that panel is a menu or a
/// button. The reason is that a slash command option is typed blind — the person
/// filling it in cannot see what the setting is now, what else it interacts with,
/// or which values are legal, and finds out only when the command is refused.
/// <br/>
/// The panels are arranged as a shallow tree rather than one long form. A form
/// would have to be submitted as a whole, which means holding a half-filled draft
/// somewhere and deciding when it expires; instead each control applies its own
/// change the moment it is used, and the panel is redrawn from the settings that
/// were actually saved. Nothing is held between two interactions, so a panel left
/// open across a restart still works, and two moderators configuring at once
/// cannot overwrite each other with stale values.
/// <br/>
/// Depth is capped at two steps from any command. Deeper nesting is what turns a
/// set of controls into a wizard.
/// </remarks>
internal static class ConfigPanels
{
    /// <summary>
    /// The identifier of the opening edge of the voting window.
    /// </summary>
    public const string OPENING = "open";

    /// <summary>
    /// The identifier of the closing edge of the voting window.
    /// </summary>
    public const string CLOSING = "close";

    /// <summary>
    /// The switch controlling whether abstentions may be cast.
    /// </summary>
    public const string ABSTAIN = "abstain";

    /// <summary>
    /// The switch controlling whether applicants may vote on themselves.
    /// </summary>
    public const string SELF_VOTE = "self";

    /// <summary>
    /// The switch controlling whether a cast vote may be changed.
    /// </summary>
    public const string VOTE_CHANGE = "change";

    /// <summary>
    /// What each channel is for, in the order the panel shows them.
    /// </summary>
    /// <remarks>
    /// Intake and review come first because a cycle cannot open without them.
    /// </remarks>
    private static readonly Role[] Roles =
    [
        new(
            GuildChannelRole.Intake,
            "Intake",
            "Where applications are read from.",
            Required: true),
        new(
            GuildChannelRole.Review,
            "Review",
            "Where the panel votes on them.",
            Required: true),
        new(
            GuildChannelRole.Results,
            "Results",
            "Where a cycle's outcome is announced. Falls back to review.",
            Required: false),
        new(
            GuildChannelRole.Archive,
            "Archive",
            "Where decided applications are kept. Skipped when unset.",
            Required: false),
        new(
            GuildChannelRole.Log,
            "Log",
            "Where the audit trail is echoed. Kept in the database either way.",
            Required: false),
    ];

    /// <summary>
    /// Builds the row of buttons that turns <c>/config show</c> into a way in.
    /// </summary>
    /// <returns>The component.</returns>
    public static ActionRowBuilder Navigation() => Layout.Actions(
        new ButtonBuilder("Channels", ComponentIds.CONFIG_CHANNELS, ButtonStyle.Primary),
        new ButtonBuilder("Passing rules", ComponentIds.CONFIG_POLICY, ButtonStyle.Primary),
        new ButtonBuilder("Schedule", ComponentIds.CONFIG_SCHEDULE, ButtonStyle.Primary),
        new ButtonBuilder("Voters", ComponentIds.VOTER_LIST, ButtonStyle.Secondary),
        new ButtonBuilder("Dismiss", ComponentIds.DISMISS, ButtonStyle.Secondary));

    /// <summary>
    /// Builds the panel listing every channel role and what it points at.
    /// </summary>
    /// <remarks>
    /// An overview with a button per role rather than five menus stacked together.
    /// Five channel menus in one message are hard to tell apart once their
    /// placeholders are replaced by choices, and they leave no room to say what a
    /// channel is for, or to offer clearing one.
    /// </remarks>
    /// <param name="model">The guild's current settings.</param>
    /// <param name="notice">What just changed, when something did.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Routing(GuildSettingsModel model, string? notice = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<IMessageComponentBuilder> parts =
        [
            Layout.Heading("Channel routing"),
            Layout.Text(model.IsOperational
                ? "Every stage is wired up. Pick a role to point it somewhere else."
                : "Intake and review are both required before a cycle can open."),
        ];

        if (notice is not null)
        {
            parts.Add(Layout.Text(notice));
        }

        parts.Add(Layout.Rule());

        foreach (Role role in Roles)
        {
            ulong? channel = Assigned(model, role.Value);

            parts.Add(Layout.Row(
                $"**{role.Label}** · {Display.Channel(channel)}\n-# {role.Detail}",
                channel is null ? "Set" : "Change",
                ComponentIds.ChannelOpen(role.Value),
                channel is null && role.Required ? ButtonStyle.Primary : ButtonStyle.Secondary));
        }

        parts.Add(Layout.Actions(
            new ButtonBuilder("Back", ComponentIds.CONFIG_HOME, ButtonStyle.Secondary),
            new ButtonBuilder("Dismiss", ComponentIds.DISMISS, ButtonStyle.Secondary)));

        return Layout.Panel(model.IsOperational ? Palette.Neutral : Palette.Caution, [.. parts]);
    }

    /// <summary>
    /// Builds the picker for a single channel role.
    /// </summary>
    /// <remarks>
    /// The menu opens with the channel already selected where one is set, so the
    /// panel shows the current answer rather than asking as though nothing had
    /// been decided.
    /// </remarks>
    /// <param name="role">The role being pointed somewhere.</param>
    /// <param name="model">The guild's current settings.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Channel(GuildChannelRole role, GuildSettingsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        Role described = Describe(role);
        ulong? current = Assigned(model, role);

        SelectMenuBuilder picker = new SelectMenuBuilder()
            .WithCustomId(ComponentIds.ChannelPick(role))
            .WithType(ComponentType.ChannelSelect)
            .WithChannelTypes(Selectable(role))
            .WithPlaceholder("Choose a channel")
            .WithMinValues(1)
            .WithMaxValues(1);

        if (current is { } channelId)
        {
            _ = picker.AddDefaultValue(channelId, SelectDefaultValueType.Channel);
        }

        List<ButtonBuilder> buttons =
        [
            new ButtonBuilder("Back to channels", ComponentIds.CONFIG_CHANNELS, ButtonStyle.Secondary),
        ];

        // Offered only where it can succeed. Intake and review are what readiness
        // is made of, so the command refuses to clear them, and a button that
        // always fails is worse than one that is not there.
        if (!described.Required && current is not null)
        {
            buttons.Add(new ButtonBuilder(
                "Clear",
                ComponentIds.ChannelClear(role),
                ButtonStyle.Danger));
        }

        return Layout.Panel(
            Palette.Neutral,
            Layout.Heading(described.Label),
            Layout.Text(described.Detail),
            Layout.Fields(("Currently", Display.Channel(current))),
            Layout.Rule(),
            new ActionRowBuilder().WithSelectMenu(picker),
            Layout.Note(Accepts(role)),
            Layout.Actions([.. buttons]));
    }

    /// <summary>
    /// Builds the panel governing what it takes for an application to pass.
    /// </summary>
    /// <param name="model">The guild's current settings.</param>
    /// <param name="notice">What just changed, when something did.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Policy(GuildSettingsModel model, string? notice = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<SelectMenuOptionBuilder> thresholds = [];

        foreach (int percent in Choices.Thresholds)
        {
            thresholds.Add(new SelectMenuOptionBuilder()
                .WithLabel(Choices.ThresholdLabel(percent))
                .WithValue(percent.ToString(CultureInfo.InvariantCulture))
                .WithDefault(percent == model.ApprovalPercentage));
        }

        List<SelectMenuOptionBuilder> quorums = [];

        foreach (int quorum in Choices.Quorums)
        {
            quorums.Add(new SelectMenuOptionBuilder()
                .WithLabel(Choices.QuorumLabel(quorum))
                .WithValue(quorum.ToString(CultureInfo.InvariantCulture))
                .WithDefault(quorum == model.Quorum));
        }

        List<IMessageComponentBuilder> parts =
        [
            Layout.Heading("Passing rules"),
            Layout.Text(string.Create(
                CultureInfo.InvariantCulture,
                $"An application passes on **{model.ApprovalPercentage}%** approval once at least "
                + $"**{model.Quorum}** deciding vote(s) are in. Anything short of that is denied "
                + $"when the cycle is finalised.")),
        ];

        if (notice is not null)
        {
            parts.Add(Layout.Text(notice));
        }

        parts.Add(Layout.Rule());
        parts.Add(new ActionRowBuilder().WithSelectMenu(new SelectMenuBuilder()
            .WithCustomId(ComponentIds.POLICY_RATIO)
            .WithOptions(thresholds)
            .WithPlaceholder("Approval threshold")
            .WithMinValues(1)
            .WithMaxValues(1)));
        parts.Add(new ActionRowBuilder().WithSelectMenu(new SelectMenuBuilder()
            .WithCustomId(ComponentIds.POLICY_QUORUM)
            .WithOptions(quorums)
            .WithPlaceholder("Votes required before a decision counts")
            .WithMinValues(1)
            .WithMaxValues(1)));
        parts.Add(Layout.Note("Press a switch to flip it."));
        parts.Add(Layout.Actions(
            Switch("Abstentions", ABSTAIN, model.AllowAbstain),
            Switch("Self-votes", SELF_VOTE, model.AllowSelfVote),
            Switch("Changing a vote", VOTE_CHANGE, model.AllowVoteChange)));
        parts.Add(Layout.Actions(
            new ButtonBuilder("Back", ComponentIds.CONFIG_HOME, ButtonStyle.Secondary),
            new ButtonBuilder("Dismiss", ComponentIds.DISMISS, ButtonStyle.Secondary)));

        return Layout.Panel(Palette.Neutral, [.. parts]);
    }

    /// <summary>
    /// Builds the panel governing when cycles run.
    /// </summary>
    /// <remarks>
    /// The days sit here because a multi-select expresses them exactly. The two
    /// ends of the window and the time zone are behind buttons instead: an hour
    /// and a minute are two menus each, and five menus in one message is more than
    /// Discord will lay out in a row and more than anybody can read at once.
    /// </remarks>
    /// <param name="model">The guild's current settings.</param>
    /// <param name="notice">What just changed, when something did.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Schedule(GuildSettingsModel model, string? notice = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<SelectMenuOptionBuilder> days = [];

        foreach (DayOfWeek day in Week)
        {
            CycleDays flag = CycleSchedule.FlagFor(day);

            days.Add(new SelectMenuOptionBuilder()
                .WithLabel(day.ToString())
                .WithValue(((int)flag).ToString(CultureInfo.InvariantCulture))
                .WithDefault(model.Days.HasFlag(flag)));
        }

        List<IMessageComponentBuilder> parts =
        [
            Layout.Heading("Schedule"),
            Layout.Fields(
                ("Days", Display.Of(model.Days)),
                ("Window", Display.Window(model.OpensAt, model.ClosesAt, model.TimeZoneId))),
        ];

        if (notice is not null)
        {
            parts.Add(Layout.Text(notice));
        }

        if (!model.IsTimeZoneKnown)
        {
            parts.Add(Layout.Text(
                $"`{model.TimeZoneId}` is not known to this machine, so every cycle is being "
                + "scheduled in UTC. Pick a zone below."));
        }

        parts.Add(Layout.Rule());
        parts.Add(new ActionRowBuilder().WithSelectMenu(new SelectMenuBuilder()
            .WithCustomId(ComponentIds.DAY_SELECT)
            .WithOptions(days)
            .WithPlaceholder("Days a cycle opens on")
            .WithMinValues(1)
            .WithMaxValues(days.Count)));
        parts.Add(Layout.Actions(
            new ButtonBuilder("Mon/Wed/Sat", ComponentIds.DayPreset("standard"), ButtonStyle.Secondary),
            new ButtonBuilder("Weekdays", ComponentIds.DayPreset("weekdays"), ButtonStyle.Secondary),
            new ButtonBuilder("Weekend", ComponentIds.DayPreset("weekend"), ButtonStyle.Secondary),
            new ButtonBuilder("Every day", ComponentIds.DayPreset("daily"), ButtonStyle.Secondary),
            new ButtonBuilder("Pause", ComponentIds.DayPreset("none"), ButtonStyle.Danger)));
        parts.Add(Layout.Actions(
            new ButtonBuilder("Opening time", ComponentIds.TimeOpen(OPENING), ButtonStyle.Primary),
            new ButtonBuilder("Closing time", ComponentIds.TimeOpen(CLOSING), ButtonStyle.Primary),
            new ButtonBuilder("Time zone", ComponentIds.ZONE_OPEN, ButtonStyle.Primary),
            new ButtonBuilder("Back", ComponentIds.CONFIG_HOME, ButtonStyle.Secondary)));

        return Layout.Panel(Palette.Neutral, [.. parts]);
    }

    /// <summary>
    /// Builds the picker for one end of the voting window.
    /// </summary>
    /// <remarks>
    /// Split into an hour and a minute because a single menu cannot hold a day's
    /// worth of times — Discord allows twenty-five options and there are far more
    /// than that. Splitting also matches how the value is thought about: the hour
    /// is the decision, and the minute is nearly always zero.
    /// </remarks>
    /// <param name="edge">Which end, as <see cref="OPENING"/> or <see cref="CLOSING"/>.</param>
    /// <param name="model">The guild's current settings.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Time(string edge, GuildSettingsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        bool opening = edge == OPENING;
        TimeOnly current = opening ? model.OpensAt : model.ClosesAt;

        List<SelectMenuOptionBuilder> hours = [];

        for (int hour = 0; hour < 24; hour++)
        {
            hours.Add(new SelectMenuOptionBuilder()
                .WithLabel(Choices.HourLabel(hour))
                .WithValue(hour.ToString(CultureInfo.InvariantCulture))
                .WithDefault(hour == current.Hour));
        }

        List<SelectMenuOptionBuilder> minutes = [];

        foreach (int minute in Choices.Minutes)
        {
            minutes.Add(new SelectMenuOptionBuilder()
                .WithLabel(Choices.MinuteLabel(minute))
                .WithValue(minute.ToString(CultureInfo.InvariantCulture))
                .WithDefault(minute == current.Minute));
        }

        return Layout.Panel(
            Palette.Neutral,
            Layout.Heading(opening ? "Opening time" : "Closing time"),
            Layout.Text(opening
                ? "When voting opens on a cycle day."
                : "When voting closes. A time earlier than the opening time runs overnight into "
                    + "the following morning."),
            Layout.Fields(
                ("Currently", string.Create(CultureInfo.InvariantCulture, $"{current:HH\\:mm}")),
                ("Zone", model.TimeZoneId)),
            Layout.Rule(),
            new ActionRowBuilder().WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId(ComponentIds.TimeHour(edge))
                .WithOptions(hours)
                .WithPlaceholder("Hour")
                .WithMinValues(1)
                .WithMaxValues(1)),
            new ActionRowBuilder().WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId(ComponentIds.TimeMinute(edge))
                .WithOptions(minutes)
                .WithPlaceholder("Minute")
                .WithMinValues(1)
                .WithMaxValues(1)),
            Layout.Actions(new ButtonBuilder(
                "Back to schedule",
                ComponentIds.CONFIG_SCHEDULE,
                ButtonStyle.Secondary)));
    }

    /// <summary>
    /// Builds the time zone picker, one region at a time.
    /// </summary>
    /// <remarks>
    /// A zone is stored as an IANA identifier rather than an offset so that a
    /// window survives a daylight saving change instead of drifting by an hour
    /// twice a year. There are several hundred of them, which is why the picker
    /// asks for a region first and pages within it.
    /// </remarks>
    /// <param name="region">The region being browsed, or nothing yet.</param>
    /// <param name="page">The zero-based page within that region.</param>
    /// <param name="model">The guild's current settings.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Zone(string? region, int page, GuildSettingsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<SelectMenuOptionBuilder> regions = [];

        foreach (string name in Choices.Regions)
        {
            regions.Add(new SelectMenuOptionBuilder()
                .WithLabel(name)
                .WithValue(name)
                .WithDefault(name == region));
        }

        List<IMessageComponentBuilder> parts =
        [
            Layout.Heading("Time zone"),
            Layout.Fields(("Currently", model.TimeZoneId)),
            Layout.Text(model.IsTimeZoneKnown
                ? "Cycle times are read in this zone, and follow it across daylight saving changes."
                : "This machine cannot resolve that zone, so cycles are being scheduled in UTC."),
            Layout.Rule(),
            new ActionRowBuilder().WithSelectMenu(new SelectMenuBuilder()
                .WithCustomId(ComponentIds.ZONE_REGION)
                .WithOptions(regions)
                .WithPlaceholder("Region")
                .WithMinValues(1)
                .WithMaxValues(1)),
        ];

        if (region is not null)
        {
            IReadOnlyList<string> zones = Choices.ZonePage(region, page);
            List<SelectMenuOptionBuilder> options = [];

            foreach (string zone in zones)
            {
                options.Add(new SelectMenuOptionBuilder()
                    .WithLabel(Choices.ZoneLabel(zone))
                    .WithValue(zone)
                    .WithDefault(zone == model.TimeZoneId));
            }

            if (options.Count > 0)
            {
                parts.Add(new ActionRowBuilder().WithSelectMenu(new SelectMenuBuilder()
                    .WithCustomId(ComponentIds.ZonePick(region, page))
                    .WithOptions(options)
                    .WithPlaceholder($"Zone in {region}")
                    .WithMinValues(1)
                    .WithMaxValues(1)));
            }

            int pages = Choices.PageCount(region);

            if (pages > 1)
            {
                parts.Add(Layout.Note(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Page {page + 1} of {pages}")));
                parts.Add(Layout.Actions(
                    new ButtonBuilder(
                        "Previous",
                        ComponentIds.ZonePage(region, page - 1),
                        ButtonStyle.Secondary,
                        isDisabled: page == 0),
                    new ButtonBuilder(
                        "Next",
                        ComponentIds.ZonePage(region, page + 1),
                        ButtonStyle.Secondary,
                        isDisabled: page >= pages - 1)));
            }
        }

        parts.Add(Layout.Actions(new ButtonBuilder(
            "Back to schedule",
            ComponentIds.CONFIG_SCHEDULE,
            ButtonStyle.Secondary)));

        return Layout.Panel(Palette.Neutral, [.. parts]);
    }

    /// <summary>
    /// Finds what a role is currently pointed at.
    /// </summary>
    /// <param name="model">The guild's current settings.</param>
    /// <param name="role">The role to read.</param>
    /// <returns>The channel, or <see langword="null"/> when unset.</returns>
    public static ulong? Assigned(GuildSettingsModel model, GuildChannelRole role)
    {
        ArgumentNullException.ThrowIfNull(model);

        return role switch
        {
            GuildChannelRole.Intake => model.IntakeChannelId,
            GuildChannelRole.Review => model.ReviewChannelId,
            GuildChannelRole.Results => model.ResultsChannelId,
            GuildChannelRole.Archive => model.ArchiveChannelId,
            GuildChannelRole.Log => model.LogChannelId,
            _ => null,
        };
    }

    /// <summary>
    /// Names a role the way the panels do.
    /// </summary>
    /// <param name="role">The role to name.</param>
    /// <returns>The label, without its emoji.</returns>
    public static string Name(GuildChannelRole role) =>
        Describe(role).Label.Split(' ', 2)[^1];

    private static Role Describe(GuildChannelRole role)
    {
        foreach (Role candidate in Roles)
        {
            if (candidate.Value == role)
            {
                return candidate;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown channel role.");
    }

    /// <summary>
    /// Lists the kinds of channel a role will accept.
    /// </summary>
    /// <remarks>
    /// Intake is the only role that takes a forum, because a forum holds no
    /// messages of its own — each post is a thread, which intake reads as one
    /// application, but which nothing can be posted into without opening a new
    /// post first. Everywhere else the bot edits and replies, so the channel has
    /// to be one that holds messages directly.
    /// </remarks>
    /// <param name="role">The role being configured.</param>
    /// <returns>The channel types to offer.</returns>
    private static ChannelType[] Selectable(GuildChannelRole role) =>
        role == GuildChannelRole.Intake
            ?
            [
                ChannelType.Text,
                ChannelType.News,
                ChannelType.Forum,
                ChannelType.PublicThread,
                ChannelType.PrivateThread,
                ChannelType.NewsThread,
            ]
            :
            [
                ChannelType.Text,
                ChannelType.News,
                ChannelType.PublicThread,
                ChannelType.PrivateThread,
                ChannelType.NewsThread,
            ];

    private static string Accepts(GuildChannelRole role) => role == GuildChannelRole.Intake
        ? "Accepts a text or announcement channel, a thread, or a forum — where each post counts "
            + "as one application."
        : "Accepts a text or announcement channel, or a thread. A forum cannot be used here "
            + "because the bot has to post and edit in place.";

    // The state is spelled out in the label rather than shown by the button's
    // colour alone. Colour is the first thing lost to a screenshot, a colourblind
    // reader, or a client theme, and a switch whose current position cannot be
    // read is worse than no switch.
    private static ButtonBuilder Switch(string label, string rule, bool allowed) => new(
        $"{label}: {(allowed ? "allowed" : "off")}",
        ComponentIds.PolicyToggle(rule, !allowed),
        allowed ? ButtonStyle.Success : ButtonStyle.Secondary);

    // Monday first, matching how a schedule is read and written, rather than the
    // Sunday-first order DayOfWeek declares.
    private static ReadOnlySpan<DayOfWeek> Week =>
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    private readonly record struct Role(
        GuildChannelRole Value,
        string Label,
        string Detail,
        bool Required);
}
