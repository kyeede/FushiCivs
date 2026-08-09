using System.Data.Common;

using Fushi.Host.Logging;
using Fushi.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Fushi.Host;

/// <summary>
/// Applies pending migrations when the host has been configured to.
/// </summary>
/// <remarks>
/// Off unless <c>Database:ApplyMigrationsOnStartup</c> says otherwise, and it
/// should stay off outside development. Two instances starting together both try,
/// and the one that loses the race fails in a way that is genuinely hard to
/// diagnose after the fact — a lock timeout during startup, with no indication
/// that a sibling caused it. Production schemas are applied deliberately, from a
/// reviewed script; see <c>docs/operations.md</c>.
/// <br/>
/// The convenience is real, though, which is why the switch exists at all: a
/// developer who has just pulled a migration should not have to remember to run a
/// script before the bot will start.
/// </remarks>
public static class MigrationExtensions
{
    /// <summary>
    /// Brings the database schema up to date, if that has been asked for.
    /// </summary>
    /// <param name="app">The built host.</param>
    /// <returns>A task that completes once the schema has been checked.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="app"/> is <see langword="null"/>.
    /// </exception>
    public static async Task MigrateIfConfiguredAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", defaultValue: false))
        {
            return;
        }

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();

        FushiDbContext context = scope.ServiceProvider.GetRequiredService<FushiDbContext>();
        ILogger logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Fushi.Host.Migrations");

        try
        {
            IEnumerable<string> pending = await context.Database.GetPendingMigrationsAsync();
            int count = pending.Count();

            if (count == 0)
            {
                HostLog.SchemaCurrent(logger);
                return;
            }

            HostLog.ApplyingMigrations(logger, count);

            await context.Database.MigrateAsync();
        }
        catch (DbException exception)
        {
            // Still fatal — a bot running against a schema it cannot reach would
            // fail every command anyway. Rewritten only because the raw exception
            // says "failed to connect to 127.0.0.1:5432" under forty frames of
            // Npgsql, and the cause is almost always a database that has not been
            // started rather than anything about the schema.
            throw new InvalidOperationException(
                "Could not reach the database to apply migrations. Start it with "
                + "`docker compose up -d --wait`, or check ConnectionStrings__Database. "
                + "Set Database:ApplyMigrationsOnStartup to false to skip this step.",
                exception);
        }
    }
}
