using Discord;
using Discord.Interactions;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Guilds;
using Fushi.Interactions.Components;
using Fushi.Interactions.Formatting;

namespace Fushi.Interactions.Modules;

/// <summary>
/// <c>/config</c> — the channels, rules, and schedule a guild runs under.
/// </summary>
/// <remarks>
/// None of these commands takes a value. Each opens a panel, and the panel's menus
/// and buttons are what write anything — which is why the module is short and
/// <see cref="ConfigComponentModule"/> is not.
/// <br/>
/// The reason for that split is that a slash command option is filled in blind.
/// Discord shows the option's name and nothing else: not what the setting is now,
/// not which values are legal, not what it interacts with. Somebody setting a
/// closing time had to know the format, the zone it would be read in, and that a
/// time before the opening time means an overnight window — and found out they had
/// guessed wrong only when the command was refused. A panel answers all three
/// before the choice is made.
/// <br/>
/// Gated on Manage Server, so the commands do not appear at all for members who
/// could not use them. Every reply is ephemeral: channel routing and voting
/// thresholds are staff business, and a channel full of configuration output is
/// noise for everybody else.
/// </remarks>
/// <param name="dispatcher">Sends requests into the application layer.</param>
[Group("config", "Configure how this server runs applications.")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[CommandContextType(InteractionContextType.Guild)]
public sealed class ConfigModule(IDispatcher dispatcher) : FushiModuleBase(dispatcher)
{
    /// <summary>
    /// Shows the guild's current configuration, with a way into each part of it.
    /// </summary>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("show", "Show and change the channels, rules, and schedule this server uses.")]
    public Task ShowAsync() => DispatchAsync(
        new GetGuildSettings(GuildId),
        settings => GuildViews.Settings(settings, ConfigPanels.Navigation()));

    /// <summary>
    /// Opens the channel routing panel.
    /// </summary>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("channels", "Choose which channels this server reads from and posts to.")]
    public Task ChannelsAsync() =>
        DispatchAsync(new GetGuildSettings(GuildId), settings => ConfigPanels.Routing(settings));

    /// <summary>
    /// Opens the panel governing what it takes for an application to pass.
    /// </summary>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("policy", "Set the approval threshold, quorum, and voting rules.")]
    public Task PolicyAsync() =>
        DispatchAsync(new GetGuildSettings(GuildId), settings => ConfigPanels.Policy(settings));

    /// <summary>
    /// Opens the panel governing when cycles run.
    /// </summary>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("schedule", "Set which days cycles open on, when voting runs, and in which zone.")]
    public Task ScheduleAsync() =>
        DispatchAsync(new GetGuildSettings(GuildId), settings => ConfigPanels.Schedule(settings));

    /// <summary>
    /// Allows cycles to open.
    /// </summary>
    /// <returns>A task that completes once the reply has been sent.</returns>
    [SlashCommand("enable", "Allow cycles to open in this server.")]
    public Task EnableAsync() =>
        DispatchAsync(
            new SetGuildEnabled(GuildId, ActorId, Enabled: true),
            "Enabled",
            "Cycles will open on the configured schedule.");

    /// <summary>
    /// Stops new cycles opening.
    /// </summary>
    /// <remarks>
    /// Behind a confirmation because it stops the bot doing the thing it is for,
    /// silently — nothing fails, cycles simply never open, which is a difficult
    /// symptom to trace back to a command somebody ran a week ago.
    /// </remarks>
    /// <returns>A task that completes once the prompt has been sent.</returns>
    [SlashCommand("disable", "Stop new cycles opening. Keeps all configuration and history.")]
    public Task DisableAsync() =>
        ConfirmAsync(
            "Disable this server?",
            "No new cycle will open until it is enabled again. Configuration, submissions, "
            + "grants, and history are all kept, and any cycle already open is unaffected.",
            ComponentIds.Confirm("disable"),
            "Disable");
}
