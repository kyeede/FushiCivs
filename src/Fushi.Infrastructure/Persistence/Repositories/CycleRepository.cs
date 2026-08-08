using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Identifiers;
using Fushi.Core.Utilities.Paging;

using Microsoft.EntityFrameworkCore;

namespace Fushi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads and stores voting cycles in PostgreSQL.
/// </summary>
/// <param name="context">The database session.</param>
internal sealed class CycleRepository(FushiDbContext context) : ICycleRepository
{
    /// <inheritdoc/>
    public Task<Cycle?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Cycles.FirstOrDefaultAsync(cycle => cycle.Id == id, cancellationToken);

    /// <inheritdoc/>
    public Task<Cycle?> FindByCodeAsync(
        ulong guildId,
        ShortCode code,
        CancellationToken cancellationToken = default)
        => context.Cycles
            .FirstOrDefaultAsync(
                cycle => cycle.GuildId == guildId && cycle.Code == code,
                cancellationToken);

    /// <inheritdoc/>
    public Task<Cycle?> FindOpenAsync(
        ulong guildId,
        CancellationToken cancellationToken = default)
        // The submissions and their votes are loaded together, because every caller
        // that asks for the open cycle is about to read a tally. Two Include levels
        // over a set bounded by one cycle's submissions is cheaper than the round
        // trip per submission that lazy loading would produce.
        => context.Cycles
            .Include(cycle => cycle.Submissions)
                .ThenInclude(submission => submission.Votes)
            .FirstOrDefaultAsync(
                cycle => cycle.GuildId == guildId && cycle.Status == CycleStatus.Open,
                cancellationToken);

    /// <inheritdoc/>
    public Task<Cycle?> FindByDateAsync(
        ulong guildId,
        DateOnly date,
        CancellationToken cancellationToken = default)
        => context.Cycles
            .FirstOrDefaultAsync(
                cycle => cycle.GuildId == guildId && cycle.ScheduledDate == date,
                cancellationToken);

    /// <inheritdoc/>
    public Task<Cycle?> FindWithSubmissionsAsync(
        ulong guildId,
        ShortCode code,
        CancellationToken cancellationToken = default)
        => context.Cycles
            .Include(cycle => cycle.Submissions)
                .ThenInclude(submission => submission.Votes)
            .FirstOrDefaultAsync(
                cycle => cycle.GuildId == guildId && cycle.Code == code,
                cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Cycle>> ListDueToFinaliseAsync(
        CancellationToken cancellationToken = default)
        => await context.Cycles
            .Include(cycle => cycle.Submissions)
                .ThenInclude(submission => submission.Votes)
            .Where(cycle => cycle.Status == CycleStatus.Closed)
            .OrderBy(cycle => cycle.ClosesAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<Page<Cycle>> ListAsync(
        ulong guildId,
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Cycle> query = context.Cycles
            .AsNoTracking()
            .Where(cycle => cycle.GuildId == guildId);

        int total = await query.CountAsync(cancellationToken);

        List<Cycle> items = await query
            // Submissions but not their votes. The outcome counts a listing needs are
            // columns on the submission; the votes would multiply the rows fetched to
            // answer a question already settled.
            .Include(cycle => cycle.Submissions)
            .OrderByDescending(cycle => cycle.ScheduledDate)
            .ThenByDescending(cycle => cycle.CreatedAt)
            .Skip(request.Skip)
            .Take(request.Size)
            .ToListAsync(cancellationToken);

        return Page<Cycle>.From(items, request, total);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Cycle>> ListDueToOpenAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
        => await context.Cycles
            .Where(cycle => cycle.Status == CycleStatus.Scheduled && cycle.OpensAt <= asOf)
            .OrderBy(cycle => cycle.OpensAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Cycle>> ListDueToCloseAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
        => await context.Cycles
            .Where(cycle => cycle.Status == CycleStatus.Open && cycle.ClosesAt <= asOf)
            .OrderBy(cycle => cycle.ClosesAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public void Add(Cycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        context.Cycles.Add(cycle);
    }
}
