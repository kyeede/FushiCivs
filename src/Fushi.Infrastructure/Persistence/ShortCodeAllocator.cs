using Fushi.Application.Abstractions.Persistence;
using Fushi.Core.Errors;
using Fushi.Core.Exceptions;
using Fushi.Core.Identifiers;

using Microsoft.EntityFrameworkCore;

namespace Fushi.Infrastructure.Persistence;

/// <summary>
/// Generates short codes that no existing row in the guild already uses.
/// </summary>
/// <remarks>
/// The code space is 2^30, a little over a billion. The birthday bound says a
/// fifty-fifty chance of one collision arrives at roughly 38,000 codes, which for a
/// single guild's submissions is far beyond anything realistic — but "far beyond
/// realistic" is not "impossible", and a collision would surface as an opaque
/// constraint violation in the middle of intake.
/// <br/>
/// So each candidate is checked before use. The check is not a guarantee on its own:
/// two allocations running concurrently can both find the same code free before
/// either inserts. The unique index is what actually decides; callers that need to
/// recover from a collision must retry the surrounding command.
/// </remarks>
/// <param name="context">The database session.</param>
internal sealed class ShortCodeAllocator(FushiDbContext context) : IShortCodeAllocator
{
    /// <summary>
    /// How many candidates to try before giving up.
    /// </summary>
    /// <remarks>
    /// Eight failures in a row means either the guild holds a substantial fraction of
    /// a billion codes or something is badly wrong. Both warrant an operator looking
    /// at it rather than the process spinning.
    /// </remarks>
    private const int MAX_ATTEMPTS = 8;

    /// <inheritdoc/>
    public Task<ShortCode> AllocateForSubmissionAsync(
        ulong guildId,
        CancellationToken cancellationToken = default)
        => AllocateAsync(
            guildId,
            static (session, guild, code, token) => session.Submissions
                .IgnoreQueryFilters()
                .AnyAsync(
                    submission => submission.GuildId == guild && submission.Code == code,
                    token),
            cancellationToken);

    /// <inheritdoc/>
    public Task<ShortCode> AllocateForCycleAsync(
        ulong guildId,
        CancellationToken cancellationToken = default)
        => AllocateAsync(
            guildId,
            static (session, guild, code, token) => session.Cycles
                .IgnoreQueryFilters()
                .AnyAsync(cycle => cycle.GuildId == guild && cycle.Code == code, token),
            cancellationToken);

    private async Task<ShortCode> AllocateAsync(
        ulong guildId,
        Func<FushiDbContext, ulong, ShortCode, CancellationToken, Task<bool>> isTaken,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            ShortCode candidate = ShortCode.New();

            if (!await isTaken(context, guildId, candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new FushiException(Error.Unexpected(
            "ShortCode.Exhausted",
            $"No free code was found for guild {guildId} in {MAX_ATTEMPTS} attempts. The "
            + "code space for this guild is unexpectedly dense and needs investigation."));
    }
}
