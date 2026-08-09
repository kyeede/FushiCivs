using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Application.Features.Submissions;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Results;
using Fushi.Gateway;
using Fushi.Host.Logging;
using Fushi.Host.Options;

using Microsoft.Extensions.Options;

namespace Fushi.Host.Scheduling;

/// <summary>
/// Reads each guild's intake channel and turns new messages into submissions
/// waiting for the next cycle.
/// </summary>
/// <remarks>
/// Polling rather than reacting to the message event, which is the less obvious
/// choice and the deliberate one. A gateway event is delivered once: if the bot is
/// restarting or disconnected when somebody posts, that application is simply
/// never seen, and nothing about the system would ever notice it was missing. A
/// sweep reads history, so a gap closes itself the moment the bot is back.
/// <br/>
/// It costs a history request per guild per interval, and the capture command
/// discards messages it has already recorded, so nothing is captured twice. That
/// makes a sweep safe to repeat and safe to interrupt.
/// </remarks>
/// <param name="scopes">Opens a scope per sweep, so each sweep has its own session.</param>
/// <param name="readiness">Delays the first sweep until Discord is connected.</param>
/// <param name="clock">Drives the timer.</param>
/// <param name="options">How often to sweep, and how much to read.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class IntakeSweeper(
    IServiceScopeFactory scopes,
    IGatewayReadiness readiness,
    TimeProvider clock,
    IOptions<SchedulerOptions> options,
    ILogger<IntakeSweeper> logger)
    : BackgroundService
{
    // The same reasoning as the scheduler's: an audit entry attributed to a real
    // snowflake would claim a person collected these, and nobody did.
    private const ulong SYSTEM_ACTOR = 0uL;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IntakeEnabled)
        {
            return;
        }

        await readiness.WaitForReadyAsync(stoppingToken);

        var period = TimeSpan.FromSeconds(options.Value.IntakeSeconds);
        using PeriodicTimer timer = new(period, clock);

        do
        {
            await SweepAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            IGuildRepository guilds = scope.ServiceProvider.GetRequiredService<IGuildRepository>();

            IReadOnlyList<Guild> operational = await guilds.ListOperationalAsync(cancellationToken);

            foreach (Guild guild in operational)
            {
                await SweepGuildAsync(dispatcher, guild.Id, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // As with the scheduler: a sweep that throws must
        // not take the service down, because the failure is nearly always
        // transient and the consequence of stopping is that applications silently
        // stop being collected. The suppression is scoped to this one method
        // rather than the file.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            HostLog.IntakeFaulted(logger, exception);
        }
    }

    private async Task SweepGuildAsync(
        IDispatcher dispatcher,
        ulong guildId,
        CancellationToken cancellationToken)
    {
        Result<IntakeSummaryModel> swept = await dispatcher.SendAsync(
            new CaptureSubmissions(guildId, SYSTEM_ACTOR, Limit: options.Value.IntakeBatchSize),
            cancellationToken);

        if (swept.IsFailure)
        {
            HostLog.IntakeQuiet(logger, guildId, swept.Error.Code);
            return;
        }

        IntakeSummaryModel summary = swept.Value;

        // A sweep that captured nothing is the normal case — it happens every
        // interval that nobody applied — so it is logged at debug and the
        // information level stays a record of applications actually arriving.
        if (summary.Captured == 0)
        {
            HostLog.IntakeQuiet(logger, guildId, "nothing new");
            return;
        }

        HostLog.IntakeSwept(
            logger,
            summary.Captured,
            guildId,
            summary.Skipped,
            summary.MessagesRead);
    }
}
