namespace Fushi.Application.Abstractions.Persistence;

/// <summary>
/// Commits the changes a handler has made, as one atomic step.
/// </summary>
/// <remarks>
/// Handlers never call this themselves. The pipeline's unit-of-work behaviour
/// saves once, after a command handler returns a successful result, and saves
/// nothing when it returns a failure. That single rule removes a whole class of
/// bug: a handler cannot leave half a change committed by returning early, and
/// cannot forget to save at all.
/// <br/>
/// Queries never reach this interface, which is why a read path cannot
/// accidentally write.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Writes every pending change.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancelled when the caller stops waiting.
    /// </param>
    /// <returns>The number of rows affected.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs an operation inside a database transaction, retrying it if the
    /// provider reports a transient fault.
    /// </summary>
    /// <remarks>
    /// The operation may run more than once, so it must not depend on state
    /// mutated by a previous attempt. Retrying is what makes a short-code
    /// collision recoverable: the unique index rejects the insert, and the
    /// second attempt allocates a different code.
    /// </remarks>
    /// <typeparam name="TResult">The type the operation produces.</typeparam>
    /// <param name="operation">The work to perform transactionally.</param>
    /// <param name="cancellationToken">
    /// Cancelled when the caller stops waiting.
    /// </param>
    /// <returns>The value the operation produced.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="operation"/> is <see langword="null"/>.
    /// </exception>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
