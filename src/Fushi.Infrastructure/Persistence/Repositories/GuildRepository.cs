using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Core.Entities.Guilds;

using Microsoft.EntityFrameworkCore;

namespace Fushi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads and stores guild configuration in PostgreSQL.
/// </summary>
/// <param name="context">The database session.</param>
internal sealed class GuildRepository(FushiDbContext context) : IGuildRepository
{
    /// <inheritdoc/>
    public Task<Guild?> FindAsync(ulong guildId, CancellationToken cancellationToken = default)
        => context.Guilds
            .FirstOrDefaultAsync(guild => guild.Id == guildId, cancellationToken);

    /// <inheritdoc/>
    public Task<Guild?> FindWithPermissionsAsync(
        ulong guildId,
        CancellationToken cancellationToken = default)
        => context.Guilds
            .Include(guild => guild.VotingPermissions)
            .FirstOrDefaultAsync(guild => guild.Id == guildId, cancellationToken);

    /// <inheritdoc/>
    public async Task<Guild> GetOrCreateAsync(
        ulong guildId,
        DateTimeOffset now,
        ulong actorId,
        CancellationToken cancellationToken = default)
    {
        // Loads the grants as well, because almost every caller that needs to create
        // a guild goes on to read or change them, and a second round trip to fetch a
        // collection that is certainly empty would be pure waste.
        Guild? existing = await FindWithPermissionsAsync(guildId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        Guild guild = new(guildId, now, actorId);
        context.Guilds.Add(guild);

        return guild;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guild>> ListOperationalAsync(
        CancellationToken cancellationToken = default)
        // Guild.IsOperational cannot be translated, since it is computed from
        // Channels.IsReady which is itself computed and unmapped. The predicate is
        // therefore spelled out against the stored columns. Keeping it here rather
        // than filtering in memory matters: this runs for every scheduler tick, and
        // the alternative is loading every guild the bot has ever joined.
        => await context.Guilds
            .AsNoTracking()
            .Where(guild => guild.IsEnabled
                && guild.Channels.IntakeChannelId != null
                && guild.Channels.ReviewChannelId != null)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public void Add(Guild guild)
    {
        ArgumentNullException.ThrowIfNull(guild);

        context.Guilds.Add(guild);
    }
}
