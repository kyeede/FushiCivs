using Discord;
using Discord.WebSocket;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fushi.Host.Health;

/// <summary>
/// Reports whether the bot is connected to Discord.
/// </summary>
/// <remarks>
/// Readiness only. A disconnected gateway must never fail liveness, because
/// restarting the process is precisely the wrong response — Discord rate-limits
/// identifies, so a restart loop against a refused connection takes longer to
/// recover than doing nothing would.
/// <br/>
/// Reconnecting is reported as degraded rather than unhealthy. Discord.Net resumes
/// a dropped session on its own within seconds, and treating every blip as an
/// outage would page somebody for something that fixed itself before they read
/// the message.
/// </remarks>
/// <param name="client">The socket client to inspect.</param>
internal sealed class GatewayHealthCheck(DiscordSocketClient client) : IHealthCheck
{
    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ConnectionState state = client.ConnectionState;

        HealthCheckResult result = state switch
        {
            ConnectionState.Connected => HealthCheckResult.Healthy("Connected to Discord."),
            ConnectionState.Connecting => HealthCheckResult.Degraded("Connecting to Discord."),
            ConnectionState.Disconnecting => HealthCheckResult.Degraded(
                "Disconnecting from Discord."),
            ConnectionState.Disconnected => HealthCheckResult.Unhealthy(
                "Not connected to Discord."),
            _ => HealthCheckResult.Unhealthy("The Discord connection is in an unknown state."),
        };

        return Task.FromResult(result);
    }
}
