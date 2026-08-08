using Fushi.Core.Abstractions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fushi.Infrastructure.Persistence;

/// <summary>
/// Stamps creation, modification, and deletion times as changes are saved.
/// </summary>
/// <remarks>
/// The domain already records who acted and when through the <c>Mark</c> methods
/// on <see cref="AuditableEntity{TId}"/>, and that remains the path a command
/// should take: only the handler knows which Discord user is responsible, and an
/// interceptor has no way to find out. What an interceptor can do is guarantee
/// the <em>times</em> are present, because it runs after every handler and sees
/// every entity on its way to the database.
/// <br/>
/// So this fills gaps rather than overriding decisions. A stamp the domain
/// already wrote is left alone; a stamp it omitted is written here. The result is
/// that forgetting <see cref="AuditableEntity{TId}.MarkUpdated"/> costs the
/// actor's identity but never leaves a row claiming it was last touched at some
/// earlier, wrong instant.
/// <br/>
/// The clock is a <see cref="TimeProvider"/> so a test can drive these stamps
/// deterministically instead of racing the wall clock.
/// </remarks>
/// <param name="clock">Supplies the instant each stamp records.</param>
internal sealed class AuditStampInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Stamp(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Stamp(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();

        // Deleting an entry mutates the change tracker, so the entries are taken as
        // a list first rather than iterated lazily.
        foreach (EntityEntry entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is EntityState.Added && entry.Entity is ICreatable)
            {
                StampCreation(entry, now);
            }
            else if (entry.State is EntityState.Modified && entry.Entity is IUpdatable)
            {
                StampModification(entry, now);
            }
            else if (entry.State is EntityState.Deleted && entry.Entity is IDeletable)
            {
                SoftDelete(entry, now);
            }
        }
    }

    private static void StampCreation(EntityEntry entry, DateTimeOffset now)
    {
        PropertyEntry createdAt = entry.Property(nameof(ICreatable.CreatedAt));

        // A constructor that took the instant has already set it. Only an entity
        // built without one arrives here still holding the default.
        if (createdAt.CurrentValue is DateTimeOffset value && value == default)
        {
            createdAt.CurrentValue = now;
        }
    }

    private static void StampModification(EntityEntry entry, DateTimeOffset now)
    {
        PropertyEntry updatedAt = entry.Property(nameof(IUpdatable.UpdatedAt));

        // Testing IsModified rather than the value is what distinguishes "the
        // handler set this" from "this still holds the stamp of a previous edit".
        if (!updatedAt.IsModified)
        {
            updatedAt.CurrentValue = now;
            updatedAt.IsModified = true;
        }
    }

    private static void SoftDelete(EntityEntry entry, DateTimeOffset now)
    {
        // Removing a row would destroy the evidence a decision was made on. The
        // delete becomes an update of the deletion stamp instead.
        //
        // Going through Unchanged rather than assigning Modified directly keeps the
        // statement narrow: only the columns marked below are written, instead of
        // every column on the row.
        entry.State = EntityState.Unchanged;

        PropertyEntry deletedAt = entry.Property(nameof(IDeletable.DeletedAt));
        deletedAt.CurrentValue ??= now;
        deletedAt.IsModified = true;

        // DeletedBy stays as the domain left it. An interceptor cannot know which
        // Discord user asked for this, which is precisely why MarkDeleted remains
        // the route a command should take.
        entry.Property(nameof(IDeletable.DeletedBy)).IsModified = true;
    }
}
