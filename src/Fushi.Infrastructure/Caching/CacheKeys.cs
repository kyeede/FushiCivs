using System.Globalization;

namespace Fushi.Infrastructure.Caching;

/// <summary>
/// Builds the keys used with <c>HybridCache</c>.
/// </summary>
/// <remarks>
/// Keys are built here rather than inline so that the set in use is enumerable and
/// the prefixes cannot drift. A mistyped prefix does not fail — it quietly creates a
/// second cache that never gets invalidated, which is among the harder bugs to
/// notice.
/// <br/>
/// What is cached, and what deliberately is not, is worth stating plainly. Query
/// read models are safe to cache: they are immutable snapshots, and a stale one is
/// visibly stale rather than subtly wrong. Entities are not cached, because an
/// entity handed back from a cache is detached from the change tracker that is
/// supposed to own it, and saving it would either fail or write over somebody else's
/// change. Role membership is not cached either, for a different reason: it decides
/// whether somebody may vote, and a revoked role has to take effect at once rather
/// than when a lifetime happens to elapse.
/// </remarks>
public static class CacheKeys
{
    private const string PREFIX = "fushi";

    /// <summary>
    /// The tag applied to every entry belonging to one guild.
    /// </summary>
    /// <remarks>
    /// Tagging by guild is what makes invalidation tractable. A configuration change
    /// evicts everything for that guild in one call, rather than requiring the code
    /// that made the change to know every key that might have depended on it.
    /// </remarks>
    /// <param name="guildId">The guild.</param>
    /// <returns>The tag.</returns>
    public static string GuildTag(ulong guildId)
        => string.Create(CultureInfo.InvariantCulture, $"{PREFIX}:guild:{guildId}");

    /// <summary>
    /// The key for a guild's resolved settings.
    /// </summary>
    /// <param name="guildId">The guild.</param>
    /// <returns>The key.</returns>
    public static string GuildSettings(ulong guildId)
        => string.Create(CultureInfo.InvariantCulture, $"{PREFIX}:guild:{guildId}:settings");

    /// <summary>
    /// The key for a guild's current cycle status.
    /// </summary>
    /// <param name="guildId">The guild.</param>
    /// <returns>The key.</returns>
    public static string CycleStatus(ulong guildId)
        => string.Create(CultureInfo.InvariantCulture, $"{PREFIX}:guild:{guildId}:cycle-status");

    /// <summary>
    /// The key for one submission's read model.
    /// </summary>
    /// <param name="guildId">The guild the submission belongs to.</param>
    /// <param name="code">The submission's public code.</param>
    /// <returns>The key.</returns>
    public static string Submission(ulong guildId, string code)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{PREFIX}:guild:{guildId}:submission:{code}");
}
