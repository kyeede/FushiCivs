using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Features.Guilds;
using Fushi.Core.Results;
using Fushi.Gateway;
using Fushi.Host.Logging;
using Fushi.Host.Options;

using Microsoft.Extensions.Options;

namespace Fushi.Host.Scheduling;

/// <summary>
/// Makes sure every guild the bot is in has a configuration row.
/// </summary>
/// <remarks>
/// The same convergent shape as the scheduler and the intake sweeper, and for the
/// same reason. Reacting to the guild-join event alone would cover the case where
/// the bot is added while it happens to be running, and miss every other one: a
/// server added during a restart, during an outage, or before this service ever
/// existed would never be registered, and nothing would notice. A pass that asks
/// Discord what it is actually in cannot develop that kind of blind spot, because
/// it does not depend on having witnessed anything.
/// <br/>
/// Each pass is idempotent. Registering a guild that already has a row is a no-op,
/// so a pass is safe to repeat, safe to interrupt, and safe to run alongside a
/// configuration command creating the same row — the repository resolves that by
/// looking before it inserts.
/// <br/>
/// The first pass runs once the gateway is ready, which is the moment the socket
/// cache is populated and the question can be answered at all.
/// </remarks>
/// <param name="scopes">Opens a scope per pass, so each pass has its own session.</param>
/// <param name="readiness">Delays the first pass until Discord is connected.</param>
/// <param name="clock">Drives the timer.</param>
/// <param name="options">How often to run, and whether to run at all.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class GuildRegistrar(
    IServiceScopeFactory scopes,
    IGatewayReadiness readiness,
    TimeProvider clock,
    IOptions<SchedulerOptions> options,
    ILogger<GuildRegistrar> logger)
    : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RegistrationEnabled)
        {
            return;
        }

        await readiness.WaitForReadyAsync(stoppingToken);

        var period = TimeSpan.FromSeconds(options.Value.RegistrationSeconds);
        using PeriodicTimer timer = new(period, clock);

        do
        {
            await RegisterAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

            Result<GuildRegistrationModel> registered = await dispatcher.SendAsync(
                new RegisterGuilds(),
                cancellationToken);

            if (registered.IsFailure)
            {
                // Nearly always the gateway being between sessions, which resolves
                // itself. Reported rather than swallowed because the other cause —
                // a token that has stopped working — looks identical from here and
                // does not resolve itself.
                HostLog.RegistrationSkipped(logger, registered.Error.Code);
                return;
            }

            GuildRegistrationModel summary = registered.Value;

            // A pass that created nothing is the normal case: it happens every
            // interval after the first. Keeping it at debug leaves the information
            // level as a record of guilds actually being taken on.
            if (summary.Registered == 0)
            {
                HostLog.GuildsCurrent(logger, summary.Present);
                return;
            }

            HostLog.GuildsRegistered(logger, summary.Registered, summary.Present);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // As with the scheduler and the sweeper: a pass that
        // throws must not take the service down, because the failure is nearly
        // always transient and the consequence of stopping is that new servers
        // silently never get registered. The suppression is scoped to this one
        // method rather than the file.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            HostLog.RegistrationFaulted(logger, exception);
        }
    }
}
