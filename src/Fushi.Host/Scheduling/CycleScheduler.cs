using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Application.Features.Cycles;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Errors;
using Fushi.Core.Identifiers;
using Fushi.Core.Results;
using Fushi.Gateway;
using Fushi.Host.Logging;
using Fushi.Host.Options;

using Microsoft.Extensions.Options;

namespace Fushi.Host.Scheduling;

/// <summary>
/// Opens, closes, and finalises voting cycles on the schedule each guild
/// configured.
/// </summary>
/// <remarks>
/// Convergent rather than event-driven, and the distinction is the whole design.
/// Nothing here sets a timer for a guild's opening instant. Each pass asks what
/// the current state of the world is and moves whatever is out of place, so a
/// process that was restarted, suspended, or disconnected across an opening
/// corrects itself on the next pass instead of having missed a one-shot timer that
/// nothing will fire again.
/// <br/>
/// The cost is that a transition can be up to one pass late. That is the right
/// trade for a voting window measured in hours, and it is why the docs describe a
/// cycle opening at 10:00:18 as working correctly.
/// <br/>
/// Every transition it asks for is idempotent — asking to open a cycle that is
/// already open is refused as a conflict, not carried out twice — which is what
/// makes a repeated pass safe and lets a pass that died halfway be completed by
/// the next one.
/// </remarks>
/// <param name="scopes">Opens a scope per pass, so each pass has its own session.</param>
/// <param name="readiness">Delays the first pass until Discord is connected.</param>
/// <param name="clock">Supplies the current instant and drives the timer.</param>
/// <param name="options">How often to wake.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class CycleScheduler(
    IServiceScopeFactory scopes,
    IGatewayReadiness readiness,
    TimeProvider clock,
    IOptions<SchedulerOptions> options,
    ILogger<CycleScheduler> logger)
    : BackgroundService
{
    /// <summary>
    /// The actor recorded against a transition nobody asked for.
    /// </summary>
    /// <remarks>
    /// Zero rather than the bot's own user identifier, and the cycle commands
    /// accept it deliberately: an audit entry attributed to a real snowflake
    /// implies a person made a decision, and nobody did. The bot's identifier is
    /// also not known until the gateway connects, which would make the value
    /// depend on startup order.
    /// </remarks>
    private const ulong SYSTEM_ACTOR = 0uL;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Opening a cycle posts to Discord, so there is no point passing before
        // the connection exists. Waiting rather than failing the first pass keeps
        // a slow connection from filling the log with errors during startup.
        await readiness.WaitForReadyAsync(stoppingToken);

        var period = TimeSpan.FromSeconds(options.Value.TickSeconds);
        using PeriodicTimer timer = new(period, clock);

        do
        {
            await PassAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PassAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            IGuildRepository guilds = scope.ServiceProvider.GetRequiredService<IGuildRepository>();
            ICycleRepository cycles = scope.ServiceProvider.GetRequiredService<ICycleRepository>();

            DateTimeOffset now = clock.GetUtcNow();

            await OpenDueAsync(dispatcher, guilds, now, cancellationToken);
            await CloseDueAsync(dispatcher, cycles, now, cancellationToken);
            await FinaliseDueAsync(dispatcher, cycles, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A pass must not be able to kill the service. Any
        // exception that escapes here — a dropped connection, a transient database
        // fault — is one the next pass will encounter again if it is real, and one
        // that resolves itself if it is not. Stopping instead would mean no cycle
        // ever opens again until somebody notices. The suppression is scoped to
        // this one method rather than the file.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            HostLog.SchedulerFaulted(logger, exception);
        }
    }

    /// <summary>
    /// Opens a cycle for every guild whose voting window is currently in force.
    /// </summary>
    /// <remarks>
    /// The window is read from the guild's schedule rather than from a stored row,
    /// because a cycle that has not opened yet does not exist to be read. The
    /// command refuses a guild that already has one open, which is what makes
    /// running this every pass harmless.
    /// </remarks>
    /// <param name="dispatcher">Sends the command.</param>
    /// <param name="guilds">The guild store.</param>
    /// <param name="now">The instant this pass is reasoning about.</param>
    /// <param name="cancellationToken">Cancelled when the host stops.</param>
    /// <returns>A task that completes once every guild has been considered.</returns>
    private async Task OpenDueAsync(
        IDispatcher dispatcher,
        IGuildRepository guilds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guild> operational = await guilds.ListOperationalAsync(cancellationToken);

        HostLog.SchedulerPass(logger, operational.Count);

        foreach (Guild guild in operational)
        {
            if (!IsWindowOpen(guild, now))
            {
                continue;
            }

            Result<ShortCode> opened = await dispatcher.SendAsync(
                new OpenCycle(guild.Id, SYSTEM_ACTOR),
                cancellationToken);

            if (opened.IsSuccess)
            {
                HostLog.CycleOpened(logger, opened.Value, guild.Id);
                continue;
            }

            Report(guild.Id, "open a cycle", opened.Error);
        }
    }

    private async Task CloseDueAsync(
        IDispatcher dispatcher,
        ICycleRepository cycles,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Cycle> due = await cycles.ListDueToCloseAsync(now, cancellationToken);

        foreach (Cycle cycle in due)
        {
            Result closed = await dispatcher.SendAsync(
                new CloseCycle(cycle.GuildId, SYSTEM_ACTOR),
                cancellationToken);

            if (closed.IsSuccess)
            {
                HostLog.CycleClosed(logger, cycle.GuildId);
                continue;
            }

            Report(cycle.GuildId, "close the cycle", closed.Error);
        }
    }

    /// <summary>
    /// Decides the submissions in every cycle that has closed without being
    /// judged.
    /// </summary>
    /// <remarks>
    /// A separate pass rather than something chained onto closing, because the two
    /// are separate commits and the interval between them is real. A process that
    /// died after closing a cycle leaves one sitting closed and unjudged, and this
    /// is what picks it up.
    /// </remarks>
    /// <param name="dispatcher">Sends the command.</param>
    /// <param name="cycles">The cycle store.</param>
    /// <param name="cancellationToken">Cancelled when the host stops.</param>
    /// <returns>A task that completes once every cycle has been considered.</returns>
    private async Task FinaliseDueAsync(
        IDispatcher dispatcher,
        ICycleRepository cycles,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Cycle> due = await cycles.ListDueToFinaliseAsync(cancellationToken);

        foreach (Cycle cycle in due)
        {
            Result<CycleResultsModel> finalised = await dispatcher.SendAsync(
                new FinaliseCycle(cycle.GuildId, cycle.Code, SYSTEM_ACTOR),
                cancellationToken);

            if (finalised.IsSuccess)
            {
                CycleResultsModel results = finalised.Value;

                HostLog.CycleFinalised(
                    logger,
                    results.Code,
                    cycle.GuildId,
                    results.Approved,
                    results.Rejected,
                    results.Skipped);

                continue;
            }

            Report(cycle.GuildId, "finalise the cycle", finalised.Error);
        }
    }

    /// <summary>
    /// Reads whether a guild is inside its voting window right now.
    /// </summary>
    /// <remarks>
    /// A guild whose time zone the machine cannot resolve is treated as having no
    /// window. The alternative is an exception per guild per pass, and the
    /// configuration error is already reported to the administrator by the command
    /// itself when they next look.
    /// </remarks>
    /// <param name="guild">The guild to check.</param>
    /// <param name="now">The instant this pass is reasoning about.</param>
    /// <returns>Whether voting should be open.</returns>
    private static bool IsWindowOpen(Guild guild, DateTimeOffset now) =>
        guild.Schedule.TryResolveTimeZone(out _) && guild.Schedule.CurrentWindow(now) is not null;

    /// <summary>
    /// Logs a refused transition at the level its reason deserves.
    /// </summary>
    /// <remarks>
    /// Most refusals are the ordinary answer rather than a problem: a cycle is
    /// already open, nothing was queued, today is not a cycle day. Logging those at
    /// warning would produce one line per guild per pass and bury the failures that
    /// do matter, so a conflict is debug and everything else is a warning.
    /// </remarks>
    /// <param name="guildId">The guild the transition was for.</param>
    /// <param name="transition">What was being attempted, for the message.</param>
    /// <param name="error">Why it was refused.</param>
    private void Report(ulong guildId, string transition, Error error)
    {
        if (error.Type is ErrorType.Conflict or ErrorType.NotFound)
        {
            HostLog.NothingToDo(logger, guildId, transition, error.Code);
            return;
        }

        HostLog.TransitionFailed(logger, transition, guildId, error.Description);
    }
}
