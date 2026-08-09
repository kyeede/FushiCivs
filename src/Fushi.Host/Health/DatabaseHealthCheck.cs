using Fushi.Infrastructure.Persistence;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fushi.Host.Health;

/// <summary>
/// Reports whether the database can be reached.
/// </summary>
/// <remarks>
/// <c>CanConnectAsync</c> rather than a query against a table: it opens a
/// connection and closes it, which is exactly the question being asked, and it
/// stays correct if the schema changes underneath it.
/// <br/>
/// Readiness only, for the same reason as the gateway check. Restarting the bot
/// does not bring PostgreSQL back; it only adds a cold start to somebody else's
/// outage.
/// </remarks>
/// <param name="context">The database session to test.</param>
internal sealed class DatabaseHealthCheck(FushiDbContext context) : IHealthCheck
{
    /// <summary>
    /// How long the check waits before calling the database unreachable.
    /// </summary>
    /// <remarks>
    /// Npgsql's own connect timeout is fifteen seconds, which is a sensible
    /// default for a query somebody is waiting on and far too long for a probe. A
    /// supervisor polling every five seconds would have three requests in flight
    /// before the first answered, and would eventually conclude the process was
    /// hung rather than that the database was down — the opposite of what this
    /// check exists to distinguish.
    /// </remarks>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthCheckContext,
        CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        budget.CancelAfter(Budget);

        try
        {
            return await context.Database.CanConnectAsync(budget.Token)
                ? HealthCheckResult.Healthy("The database is reachable.")
                : HealthCheckResult.Unhealthy("The database could not be reached.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The budget ran out rather than the request being abandoned. That is
            // an answer — a database this slow to accept a connection cannot serve
            // an interaction inside Discord's three seconds either.
            return HealthCheckResult.Unhealthy("The database did not answer in time.");
        }
    }
}
