using System.Globalization;

using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Guilds;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Errors;
using Fushi.Core.Results;
using Fushi.Interactions.Components;
using Fushi.Interactions.Formatting;

namespace Fushi.Interactions.Modules;

/// <summary>
/// Every control on the configuration panels.
/// </summary>
/// <remarks>
/// The whole of <c>/config</c> arrives here, because none of those commands takes
/// a value: each opens a panel, and the panel's menus and buttons are what
/// actually write anything.
/// <br/>
/// Two rules hold throughout. A control writes exactly the one setting it names
/// and leaves the rest alone, so an abandoned panel cannot leave a guild half
/// configured. And every handler ends by redrawing the panel from settings read
/// back out of the database rather than from what it just sent — so what is on
/// screen is what was saved, including when a validator quietly adjusted it or
/// somebody else changed something in the meantime.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
[CommandContextType(InteractionContextType.Guild)]
public sealed class ConfigComponentModule(IDispatcher dispatcher) : ComponentModuleBase(dispatcher)
{
    /// <summary>
    /// Returns to the configuration overview.
    /// </summary>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.CONFIG_HOME)]
    public Task HomeAsync() =>
        RefreshAsync(settings => GuildViews.Settings(settings, ConfigPanels.Navigation()));

    /// <summary>
    /// Opens the channel routing panel.
    /// </summary>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.CONFIG_CHANNELS)]
    public Task RoutingAsync() => RefreshAsync(settings => ConfigPanels.Routing(settings));

    /// <summary>
    /// Opens the picker for one channel role.
    /// </summary>
    /// <param name="role">Which role the button named.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cfg:chan:open:*")]
    public async Task ChannelAsync(string role)
    {
        if (!TryParseRole(role, out GuildChannelRole parsed))
        {
            await ExpiredAsync();
            return;
        }

        await RefreshAsync(settings => ConfigPanels.Channel(parsed, settings));
    }

    /// <summary>
    /// Points a channel role at the channel that was picked.
    /// </summary>
    /// <param name="role">Which role the menu sets.</param>
    /// <param name="channels">What the reader picked.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cfg:chan:set:*")]
    public async Task ChannelPickedAsync(string role, IChannel[] channels)
    {
        ArgumentNullException.ThrowIfNull(channels);

        if (!TryParseRole(role, out GuildChannelRole parsed))
        {
            await ExpiredAsync();
            return;
        }

        if (channels.Length == 0)
        {
            // Discord allows a select to be emptied. Nothing was chosen, so
            // nothing should change; redrawing is the whole response.
            await RefreshAsync(settings => ConfigPanels.Channel(parsed, settings));
            return;
        }

        await ApplyChannelAsync(
            parsed,
            channels[0].Id,
            $"{ConfigPanels.Name(parsed)} now reads {Display.Channel(channels[0].Id)}.");
    }

    /// <summary>
    /// Unassigns a channel role.
    /// </summary>
    /// <param name="role">Which role the button named.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cfg:chan:clr:*")]
    public async Task ChannelClearedAsync(string role)
    {
        if (!TryParseRole(role, out GuildChannelRole parsed))
        {
            await ExpiredAsync();
            return;
        }

        await ApplyChannelAsync(parsed, null, $"{ConfigPanels.Name(parsed)} is no longer set.");
    }

    /// <summary>
    /// Opens the passing rules panel.
    /// </summary>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.CONFIG_POLICY)]
    public Task PolicyAsync() => RefreshAsync(settings => ConfigPanels.Policy(settings));

    /// <summary>
    /// Sets the share of deciding votes an application needs.
    /// </summary>
    /// <remarks>
    /// The menu carries whole percentages because that is how the bar is talked
    /// about; the domain stores a ratio, so the two differ by a factor of a
    /// hundred and the conversion belongs at exactly this boundary.
    /// </remarks>
    /// <param name="values">The percentage the reader picked.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.POLICY_RATIO)]
    public async Task ThresholdAsync(string[] values)
    {
        if (!TryParseNumber(values, out int percent))
        {
            await RefreshAsync(settings => ConfigPanels.Policy(settings));
            return;
        }

        await ApplyPolicyAsync(
            new ConfigureVotingPolicy(GuildId, ActorId, ApprovalRatio: percent / 100d),
            string.Create(CultureInfo.InvariantCulture, $"An application now passes on {percent}%."));
    }

    /// <summary>
    /// Sets how many deciding votes have to be in before a result counts.
    /// </summary>
    /// <param name="values">The count the reader picked.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.POLICY_QUORUM)]
    public async Task QuorumAsync(string[] values)
    {
        if (!TryParseNumber(values, out int quorum))
        {
            await RefreshAsync(settings => ConfigPanels.Policy(settings));
            return;
        }

        await ApplyPolicyAsync(
            new ConfigureVotingPolicy(GuildId, ActorId, Quorum: quorum),
            string.Create(
                CultureInfo.InvariantCulture,
                $"A decision now needs at least {quorum} deciding vote(s)."));
    }

    /// <summary>
    /// Flips one of the voting switches.
    /// </summary>
    /// <param name="rule">Which switch the button named.</param>
    /// <param name="allow">The value it is being moved to.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cfg:pol:tog:*:*")]
    public async Task SwitchAsync(string rule, string allow)
    {
        bool allowed = allow == "1";

        (ConfigureVotingPolicy Command, string Notice)? change = rule switch
        {
            ConfigPanels.ABSTAIN => (
                new ConfigureVotingPolicy(GuildId, ActorId, AllowAbstain: allowed),
                allowed
                    ? "Abstentions may be cast. They are recorded but do not count towards quorum."
                    : "Abstentions are no longer accepted."),
            ConfigPanels.SELF_VOTE => (
                new ConfigureVotingPolicy(GuildId, ActorId, AllowSelfVote: allowed),
                allowed
                    ? "Applicants may vote on their own application."
                    : "Applicants may no longer vote on their own application."),
            ConfigPanels.VOTE_CHANGE => (
                new ConfigureVotingPolicy(GuildId, ActorId, AllowVoteChange: allowed),
                allowed
                    ? "A cast vote may be changed while the cycle is open."
                    : "A cast vote is now final."),
            _ => null,
        };

        if (change is not { } decided)
        {
            await ExpiredAsync();
            return;
        }

        await ApplyPolicyAsync(decided.Command, decided.Notice);
    }

    /// <summary>
    /// Opens the schedule panel.
    /// </summary>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.CONFIG_SCHEDULE)]
    public Task ScheduleAsync() => RefreshAsync(settings => ConfigPanels.Schedule(settings));

    /// <summary>
    /// Sets which days a cycle opens on.
    /// </summary>
    /// <param name="days">The days the reader ticked.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.DAY_SELECT)]
    public async Task DaysAsync(string[] days)
    {
        ArgumentNullException.ThrowIfNull(days);

        CycleDays selected = days.Aggregate(CycleDays.None, (total, day) => total | ParseDay(day));

        await ApplyDaysAsync(selected);
    }

    /// <summary>
    /// Applies one of the named day patterns.
    /// </summary>
    /// <remarks>
    /// The presets are here because three of them cover most guilds, and the
    /// difference between pressing "Weekdays" and ticking five boxes is the
    /// difference between a setup that takes a moment and one that takes a
    /// minute.
    /// </remarks>
    /// <param name="preset">The pattern named on the button.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cfg:preset:*")]
    public async Task PresetAsync(string preset)
    {
        CycleDays? days = preset switch
        {
            "standard" => CycleDays.Standard,
            "weekdays" => CycleDays.Weekdays,
            "weekend" => CycleDays.Weekend,
            "daily" => CycleDays.Daily,
            "none" => CycleDays.None,
            _ => null,
        };

        if (days is null)
        {
            await ExpiredAsync();
            return;
        }

        await ApplyDaysAsync(days.Value);
    }

    /// <summary>
    /// Opens the picker for one end of the voting window.
    /// </summary>
    /// <param name="edge">Which end the button named.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cfg:time:*")]
    public async Task WindowAsync(string edge)
    {
        if (!IsEdge(edge))
        {
            await ExpiredAsync();
            return;
        }

        await RefreshAsync(settings => ConfigPanels.Time(edge, settings));
    }

    /// <summary>
    /// Moves the hour of one end of the voting window.
    /// </summary>
    /// <param name="edge">Which end the menu sets.</param>
    /// <param name="values">The hour the reader picked.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cfg:hour:*")]
    public Task HourAsync(string edge, string[] values) => ApplyTimeAsync(edge, values, hour: true);

    /// <summary>
    /// Moves the minute of one end of the voting window.
    /// </summary>
    /// <param name="edge">Which end the menu sets.</param>
    /// <param name="values">The minute the reader picked.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cfg:min:*")]
    public Task MinuteAsync(string edge, string[] values) => ApplyTimeAsync(edge, values, hour: false);

    /// <summary>
    /// Opens the time zone picker.
    /// </summary>
    /// <remarks>
    /// Opens on the region the guild is already in, so somebody correcting a zone
    /// starts beside the answer rather than at the top of an alphabet.
    /// </remarks>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.ZONE_OPEN)]
    public Task ZoneAsync() => RefreshAsync(settings =>
        ConfigPanels.Zone(Known(Choices.RegionOf(settings.TimeZoneId)), 0, settings));

    /// <summary>
    /// Shows the zones in the region that was picked.
    /// </summary>
    /// <param name="values">The region the reader picked.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction(ComponentIds.ZONE_REGION)]
    public Task RegionAsync(string[] values) => RefreshAsync(settings =>
        ConfigPanels.Zone(Known(First(values)), 0, settings));

    /// <summary>
    /// Turns to another page of a region's zones.
    /// </summary>
    /// <param name="region">The region being browsed.</param>
    /// <param name="page">The page to move to.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cfg:tzp:*:*")]
    public Task ZonePageAsync(string region, string page) => RefreshAsync(settings =>
        ConfigPanels.Zone(Known(region), ParsePage(region, page), settings));

    /// <summary>
    /// Sets the zone a guild's cycle times are read in.
    /// </summary>
    /// <param name="region">The region being browsed, carried by the menu.</param>
    /// <param name="page">The page being shown, carried by the menu.</param>
    /// <param name="values">The zone the reader picked.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    [ComponentInteraction($"{ComponentIds.PREFIX}:cfg:tzz:*:*")]
    public async Task ZonePickedAsync(string region, string page, string[] values)
    {
        if (First(values) is not { } zone)
        {
            await RefreshAsync(settings =>
                ConfigPanels.Zone(Known(region), ParsePage(region, page), settings));

            return;
        }

        await DeferAsync();

        Result result = await SendAsync(new ConfigureSchedule(GuildId, ActorId, TimeZoneId: zone));

        if (result.IsFailure)
        {
            await ReplaceAsync(Replies.Error(result.Error));
            return;
        }

        await ReplaceWithSettingsAsync(settings =>
            ConfigPanels.Schedule(settings, $"Cycle times are now read in `{zone}`."));
    }

    private async Task ApplyChannelAsync(GuildChannelRole role, ulong? channelId, string notice)
    {
        await DeferAsync();

        Result result = await SendAsync(new SetChannel(GuildId, ActorId, role, channelId));

        if (result.IsFailure)
        {
            await ReplaceAsync(Replies.Error(result.Error));
            return;
        }

        await ReplaceWithSettingsAsync(settings => ConfigPanels.Routing(settings, notice));
    }

    private async Task ApplyPolicyAsync(ConfigureVotingPolicy command, string notice)
    {
        await DeferAsync();

        Result result = await SendAsync(command);

        await ReplaceWithSettingsAsync(settings => result.IsFailure
            ? Replies.Error(result.Error)
            : ConfigPanels.Policy(settings, notice));
    }

    private async Task ApplyTimeAsync(string edge, string[] values, bool hour)
    {
        if (!IsEdge(edge))
        {
            await ExpiredAsync();
            return;
        }

        if (!TryParseNumber(values, out int part))
        {
            await RefreshAsync(settings => ConfigPanels.Time(edge, settings));
            return;
        }

        await DeferAsync();

        Result<GuildSettingsModel> settings = await SendAsync(new GetGuildSettings(GuildId));

        if (settings.IsFailure)
        {
            await ReplaceAsync(Replies.Error(settings.Error));
            return;
        }

        bool opening = edge == ConfigPanels.OPENING;
        TimeOnly current = opening ? settings.Value.OpensAt : settings.Value.ClosesAt;

        // Only the half the menu names moves. The two menus are separate controls
        // on the same panel, so setting an hour must not reset a minute somebody
        // chose a moment ago.
        TimeOnly moved = hour
            ? new TimeOnly(part, current.Minute)
            : new TimeOnly(current.Hour, part);

        Result result = await SendAsync(opening
            ? new ConfigureSchedule(GuildId, ActorId, OpensAt: moved)
            : new ConfigureSchedule(GuildId, ActorId, ClosesAt: moved));

        if (result.IsFailure)
        {
            await ReplaceAsync(Replies.Error(result.Error));
            return;
        }

        await ReplaceWithSettingsAsync(refreshed => ConfigPanels.Time(edge, refreshed));
    }

    private async Task ApplyDaysAsync(CycleDays days)
    {
        await DeferAsync();

        // CycleDays.None is refused by the schedule command, deliberately: a
        // schedule that runs on no day is an off switch that does not look like
        // one. The pause preset therefore disables the guild instead, which is
        // the honest way to say the same thing.
        Result result = days == CycleDays.None
            ? await SendAsync(new SetGuildEnabled(GuildId, ActorId, Enabled: false))
            : await SendAsync(new ConfigureSchedule(GuildId, ActorId, Days: days));

        string notice = days == CycleDays.None
            ? "Voting is paused. `/config enable` starts it again."
            : $"Cycles now open on {Display.Of(days)}.";

        await ReplaceWithSettingsAsync(settings => result.IsFailure
            ? Replies.Error(result.Error)
            : ConfigPanels.Schedule(settings, notice));
    }

    /// <summary>
    /// Defers, reads the guild's settings back, and redraws the panel from them.
    /// </summary>
    /// <param name="render">Builds the panel from what was actually saved.</param>
    /// <returns>A task that completes once the message has been changed.</returns>
    private async Task RefreshAsync(Func<GuildSettingsModel, MessageComponent> render)
    {
        await DeferAsync();
        await ReplaceWithSettingsAsync(render);
    }

    private async Task ReplaceWithSettingsAsync(Func<GuildSettingsModel, MessageComponent> render)
    {
        Result<GuildSettingsModel> settings = await SendAsync(new GetGuildSettings(GuildId));

        await ReplaceAsync(settings.IsFailure
            ? Replies.Error(settings.Error)
            : render(settings.Value));
    }

    private async Task ExpiredAsync()
    {
        await DeferAsync();
        await ReplaceAsync(Replies.Error(Expired));
    }

    private static bool TryParseRole(string role, out GuildChannelRole parsed)
    {
        parsed = role switch
        {
            "intake" => GuildChannelRole.Intake,
            "review" => GuildChannelRole.Review,
            "results" => GuildChannelRole.Results,
            "archive" => GuildChannelRole.Archive,
            "log" => GuildChannelRole.Log,
            _ => (GuildChannelRole)(-1),
        };

        return Enum.IsDefined(parsed);
    }

    private static bool TryParseNumber(string[] values, out int number)
    {
        number = 0;

        return First(values) is { } value
            && int.TryParse(value, CultureInfo.InvariantCulture, out number);
    }

    private static string? First(string[]? values) =>
        values is { Length: > 0 } ? values[0] : null;

    private static bool IsEdge(string edge) =>
        edge is ConfigPanels.OPENING or ConfigPanels.CLOSING;

    // A region carried by a component identifier came from this same list a moment
    // ago, but the machine's zone database can change under a long-lived message.
    // Falling back to no region redraws the picker at its first step rather than
    // showing an empty menu.
    private static string? Known(string? region) =>
        region is not null && Choices.Regions.Contains(region, StringComparer.Ordinal)
            ? region
            : null;

    private static int ParsePage(string region, string page) =>
        int.TryParse(page, CultureInfo.InvariantCulture, out int parsed)
            ? Math.Clamp(parsed, 0, Choices.PageCount(region) - 1)
            : 0;

    private static CycleDays ParseDay(string day) =>
        int.TryParse(day, CultureInfo.InvariantCulture, out int value)
        && Enum.IsDefined((CycleDays)value)
            ? (CycleDays)value
            : CycleDays.None;

    private static Error Expired => Error.Validation(
        "Interaction.Expired",
        "That control is from an older version of the bot. Run `/config show` to open a "
        + "current panel.");
}
