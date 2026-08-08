using Fushi.Application.Abstractions.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fushi.Infrastructure.Persistence;

/// <summary>
/// Commits work through the Entity Framework change tracker.
/// </summary>
/// <remarks>
/// A thin wrapper, deliberately. The change tracker already is a unit of work; the
/// point of the interface is not to add behaviour but to keep the application layer
/// from referencing <see cref="DbContext"/> and, with it, the whole of Entity
/// Framework.
/// <br/>
/// A single ordinary <c>SaveChanges</c> is already atomic — Entity Framework opens
/// a transaction for it when more than one statement is needed. The explicit
/// transaction here exists for the cases where a handler has to read, decide, and
/// write within one consistent view, and where a transient failure should be
/// retried rather than surfaced.
/// </remarks>
/// <param name="context">The session to commit.</param>
internal sealed class UnitOfWork(FushiDbContext context) : IUnitOfWork
{
    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // The execution strategy owns the retry loop. Opening the transaction
        // inside it rather than around it is what makes a retry start from a clean
        // transaction instead of trying to reuse one the failure already poisoned.
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(
            async token =>
            {
                await using IDbContextTransaction transaction =
                    await context.Database.BeginTransactionAsync(token);

                TResult result = await operation(token);

                await context.SaveChangesAsync(token);
                await transaction.CommitAsync(token);

                return result;
            },
            cancellationToken);
    }
}
