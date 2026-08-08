using System.ComponentModel.DataAnnotations;

namespace Fushi.Infrastructure.Options;

/// <summary>
/// How the bot caches, and whether it uses Redis to do it.
/// </summary>
/// <remarks>
/// Redis is optional. With no connection string configured, <c>HybridCache</c> runs
/// with its in-memory tier alone, which is entirely adequate for a single instance
/// and removes a service from the local development stack. Configuring one adds a
/// shared second tier so that several instances see the same cached values, and so
/// that a restart does not start cold.
/// </remarks>
public sealed class CacheOptions
{
    /// <summary>
    /// The configuration section these options bind to.
    /// </summary>
    public const string SECTION = "Cache";

    /// <summary>
    /// Gets or sets the Redis connection string.
    /// </summary>
    /// <value>
    /// A StackExchange.Redis configuration string, or <see langword="null"/> or empty
    /// to run with the in-memory tier only.
    /// </value>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// Gets or sets how long an entry stays in the in-memory tier, in seconds.
    /// </summary>
    /// <remarks>
    /// Shorter than the distributed lifetime by design. The local tier is the one
    /// that can go stale relative to other instances, so it is the one that should
    /// expire sooner.
    /// </remarks>
    [Range(1, 3600)]
    public int LocalExpirationSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets how long an entry stays in the distributed tier, in seconds.
    /// </summary>
    [Range(1, 86_400)]
    public int DistributedExpirationSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the largest payload that will be cached, in bytes.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than a target. Anything approaching it is a sign that an
    /// unbounded list is being cached, which is a bug worth failing on rather than
    /// absorbing.
    /// </remarks>
    [Range(1024, 10 * 1024 * 1024)]
    public long MaximumPayloadBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets a value indicating whether a distributed tier is configured.
    /// </summary>
    public bool UsesRedis => !string.IsNullOrWhiteSpace(RedisConnectionString);
}
