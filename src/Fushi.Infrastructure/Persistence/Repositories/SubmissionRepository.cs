using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;
using Fushi.Core.Utilities.Paging;

using Microsoft.EntityFrameworkCore;

namespace Fushi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads and stores submissions in PostgreSQL.
/// </summary>
/// <param name="context">The database session.</param>
internal sealed class SubmissionRepository(FushiDbContext context) : ISubmissionRepository
{
    /// <summary>
    /// The most choices Discord will display in an autocomplete response.
    /// </summary>
    private const int AUTOCOMPLETE_CEILING = 25;

    /// <inheritdoc/>
    public Task<Submission?> FindByCodeAsync(
        ulong guildId,
        ShortCode code,
        CancellationToken cancellationToken = default)
        => context.Submissions
            .FirstOrDefaultAsync(
                submission => submission.GuildId == guildId && submission.Code == code,
                cancellationToken);

    /// <inheritdoc/>
    public Task<Submission?> FindWithVotesByCodeAsync(
        ulong guildId,
        ShortCode code,
        CancellationToken cancellationToken = default)
        => context.Submissions
            .Include(submission => submission.Votes)
            .FirstOrDefaultAsync(
                submission => submission.GuildId == guildId && submission.Code == code,
                cancellationToken);

    /// <inheritdoc/>
    public Task<bool> ExistsForMessageAsync(
        ulong guildId,
        ulong sourceMessageId,
        CancellationToken cancellationToken = default)
        => context.Submissions
            .AnyAsync(
                submission => submission.GuildId == guildId
                    && submission.SourceMessageId == sourceMessageId,
                cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Submission>> ListQueuedAsync(
        ulong guildId,
        int limit,
        CancellationToken cancellationToken = default)
        => await context.Submissions
            .Where(submission => submission.GuildId == guildId
                && submission.Status == SubmissionStatus.Queued)
            .OrderBy(submission => submission.CreatedAt)
            .Take(Math.Max(1, limit))
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<Page<Submission>> ListAsync(
        ulong guildId,
        SubmissionStatus? status,
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Submission> query = context.Submissions
            .AsNoTracking()
            .Where(submission => submission.GuildId == guildId);

        if (status is { } wanted)
        {
            query = query.Where(submission => submission.Status == wanted);
        }

        int total = await query.CountAsync(cancellationToken);

        List<Submission> items = await query
            .OrderByDescending(submission => submission.CreatedAt)
            .Skip(request.Skip)
            .Take(request.Size)
            .ToListAsync(cancellationToken);

        return Page<Submission>.From(items, request, total);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Submission>> SearchAsync(
        ulong guildId,
        string prefix,
        int limit,
        CancellationToken cancellationToken = default)
    {
        int take = Math.Clamp(limit, 1, AUTOCOMPLETE_CEILING);

        IQueryable<Submission> query = context.Submissions
            .AsNoTracking()
            .Where(submission => submission.GuildId == guildId);

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            string normalised = NormaliseCodePrefix(prefix);

            // Two different matches, because a user typing into this box is doing one
            // of two things. If they have the code, they want that submission and
            // nothing else, so the code match is an anchored prefix that the unique
            // index serves directly. If they only remember the title, they need a
            // contains match, which cannot use an index — but it is bounded to 25 rows
            // within one guild, and Discord gives the handler about three seconds,
            // which is ample for a scan of that size.
            query = query.Where(submission =>
                EF.Functions.Like(
                    EF.Property<string>(submission, nameof(Submission.Code)),
                    normalised + "%")
                || EF.Functions.ILike(submission.Title, "%" + prefix + "%"));
        }

        return await query
            .OrderByDescending(submission => submission.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void Add(Submission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        context.Submissions.Add(submission);
    }

    /// <summary>
    /// Folds a partially typed code into the canonical form stored in the column.
    /// </summary>
    /// <remarks>
    /// Codes are stored upper case with the confusable characters already resolved,
    /// so the same folding has to be applied to the search text or a user typing
    /// <c>7k4mo</c> would match nothing while <c>7K4M0</c> matched. Characters
    /// outside the alphabet are dropped rather than rejected, since a half-typed
    /// value is the normal state of an autocomplete query.
    /// </remarks>
    /// <param name="prefix">What the user has typed.</param>
    /// <returns>The prefix as it would appear in the column.</returns>
    private static string NormaliseCodePrefix(string prefix)
    {
        Span<char> buffer = stackalloc char[Math.Min(prefix.Length, ShortCode.LENGTH)];
        int written = 0;

        foreach (char character in prefix)
        {
            if (written == buffer.Length)
            {
                break;
            }

            char folded = ShortCodeAlphabet.Normalise(char.ToUpperInvariant(character));
            if (ShortCodeAlphabet.Contains(folded))
            {
                buffer[written++] = folded;
            }
        }

        return new string(buffer[..written]);
    }
}
