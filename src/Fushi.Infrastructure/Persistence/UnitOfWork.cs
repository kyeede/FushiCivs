using Fushi.Application.Abstractions.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Fushi.Infrastructure.Persistence;

/// <summary>
/// Commits work through the Entity Framework change tracker.
/// </summary>
/// <remarks>
/// A thin wrapper, deliberately. The change tracker already is a unit of work; the
/// point of the interface is not to add behaviour but to keep the application layer
/// from referencing <see cref="DbContext"/> and, with it, the whole of Entity
/// Framework. A single ordinary <c>SaveChanges</c> is already atomic — Entity
/// Framework opens a transaction for it when more than one statement is needed.
/// </remarks>
/// <param name="context">The session to commit.</param>
internal sealed class UnitOfWork(FushiDbContext context) : IUnitOfWork
{
    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesAsync(cancellationToken);
}
