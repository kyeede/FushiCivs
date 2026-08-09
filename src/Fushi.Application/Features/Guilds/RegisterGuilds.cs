using Fushi.Application.Abstractions.Discord;
using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Application.Logging;
using Fushi.Core.Results;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Features.Guilds;

/// <summary>
/// Gives every guild the bot is in a configuration row, creating the ones that
/// are missing.
/// </summary>
/// <remarks>
/// A guild's row is what every other feature hangs off, and until it exists there
/// is nothing for a channel, a policy, or a voting grant to belong to. Creating it
/// on demand from each command would work, but it leaves a gap: the configuration
/// panels read before they write, so a server with no row cannot display the panel
/// that would create one.
/// <br/>
/// This command closes that gap by making registration a background concern rather
/// than a side effect of whichever command happened to run first. It carries no
/// parameters and needs no validator — there is nothing about "register whatever
/// is there" to assert.
/// <br/>
/// It is deliberately additive. A guild the bot has been removed from keeps its
/// row, because the only evidence of a departure is an absence from the list, and
/// an absence is precisely what a reconnect manufactures for every guild at once.
/// A stale row costs a row; acting on a false one would delete a server's channels,
/// schedule, and voting grants, and nothing in Discord could restore them.
/// </remarks>
/// <seealso cref="GuildRegistrationModel"/>
public sealed record RegisterGuilds : ICommand<GuildRegistrationModel>;

/// <summary>
/// What a registration pass found and did.
/// </summary>
/// <param name="Present">How many guilds Discord reports the bot is in.</param>
/// <param name="Registered">
/// How many of those had no row and were given one. Zero on every pass after the
/// first, which is the normal and expected result.
/// </param>
public sealed record GuildRegistrationModel(int Present, int Registered);

/// <summary>
/// Carries out <see cref="RegisterGuilds"/>.
/// </summary>
/// <param name="directory">Reports which guilds the bot is in.</param>
/// <param name="guilds">The guild store.</param>
/// <param name="clock">Supplies the instant a new row is stamped with.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class RegisterGuildsHandler(
    IGuildDirectory directory,
    IGuildRepository guilds,
    TimeProvider clock,
    ILogger<RegisterGuildsHandler> logger)
    : ICommandHandler<RegisterGuilds, GuildRegistrationModel>
{
    // Nobody asked for these rows, so attributing them to a real snowflake would
    // put a person's name against something they did not do. The entity's own
    // documentation reserves zero for exactly this.
    private const ulong SYSTEM_ACTOR = 0uL;

    /// <inheritdoc/>
    public async Task<Result<GuildRegistrationModel>> HandleAsync(
        RegisterGuilds request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<IReadOnlyCollection<ulong>> present =
            await directory.ListGuildIdsAsync(cancellationToken);

        if (present.IsFailure)
        {
            return present.Error;
        }

        DateTimeOffset now = clock.GetUtcNow();
        int registered = 0;

        foreach (ulong guildId in present.Value)
        {
            // FindAsync rather than going straight to GetOrCreateAsync, because
            // this is the path taken by every guild on every pass once the first
            // one has run. It is a keyed lookup with no join, where the create
            // path additionally loads the voting grants it is about to not need.
            // The extra lookup on a miss is paid once in a guild's lifetime.
            if (await guilds.FindAsync(guildId, cancellationToken) is not null)
            {
                continue;
            }

            await guilds.GetOrCreateAsync(guildId, now, SYSTEM_ACTOR, cancellationToken);
            registered++;

            GuildLog.Created(logger, guildId);
        }

        return new GuildRegistrationModel(present.Value.Count, registered);
    }
}
