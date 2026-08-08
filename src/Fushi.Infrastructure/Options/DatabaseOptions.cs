using System.ComponentModel.DataAnnotations;

namespace Fushi.Infrastructure.Options;

/// <summary>
/// How the bot connects to PostgreSQL.
/// </summary>
/// <remarks>
/// Validated on startup rather than on first use. A missing connection string
/// should stop the process immediately with a clear message, not surface as a
/// failure the first time somebody runs a command.
/// </remarks>
public sealed class DatabaseOptions
{
    /// <summary>
    /// The configuration section these options bind to.
    /// </summary>
    public const string SECTION = "Database";

    /// <summary>
    /// Gets or sets the Npgsql connection string.
    /// </summary>
    /// <remarks>
    /// Supplied as <c>ConnectionStrings__Database</c> in the environment. It is read
    /// from there rather than from a file so that a deployment never has a password
    /// sitting in <c>appsettings.json</c>.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how long a single command may run, in seconds.
    /// </summary>
    /// <remarks>
    /// Kept short on purpose. Every query this application issues is bounded — by a
    /// guild, by a page, or by a unique key — so a query that takes longer than this
    /// is not slow, it is wrong, and failing fast surfaces that.
    /// </remarks>
    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets how many times a transient failure is retried.
    /// </summary>
    /// <remarks>
    /// Npgsql's execution strategy decides what counts as transient: a dropped
    /// connection or a deadlock, not a constraint violation.
    /// </remarks>
    [Range(0, 10)]
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets the longest delay between retries.
    /// </summary>
    [Range(1, 120)]
    public int MaxRetryDelaySeconds { get; set; } = 10;

    /// <summary>
    /// Gets or sets a value indicating whether parameter values appear in logs and
    /// exception messages.
    /// </summary>
    /// <remarks>
    /// Invaluable when diagnosing a query and unacceptable in production, where the
    /// parameters include Discord snowflakes and application text. Guarded so that
    /// turning it on is a deliberate act.
    /// </remarks>
    public bool EnableSensitiveDataLogging { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether schema changes are applied
    /// automatically on startup.
    /// </summary>
    /// <remarks>
    /// Convenient for a local run and a poor idea anywhere else: two instances
    /// starting together would both try to migrate, and a failed migration would
    /// take the process down rather than being reviewed. Production applies
    /// migrations as a separate step through <c>build/migrate.sh</c>.
    /// </remarks>
    public bool MigrateOnStartup { get; set; }
}
