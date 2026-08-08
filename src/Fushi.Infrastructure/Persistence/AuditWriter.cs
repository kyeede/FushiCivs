using Fushi.Application.Abstractions.Persistence;
using Fushi.Core.Entities.Audits;

namespace Fushi.Infrastructure.Persistence;

/// <summary>
/// Stages audit entries for insertion alongside the change they describe.
/// </summary>
/// <remarks>
/// The entry is added to the change tracker and nothing more. It is written by the
/// same <c>SaveChanges</c> that writes the change itself, which is what guarantees
/// the two cannot disagree: a rolled-back command leaves no audit entry, and a
/// committed one cannot lack its entry.
/// <br/>
/// Writing the entry immediately, on its own connection, would be simpler to reason
/// about in isolation and wrong in exactly the cases that matter.
/// </remarks>
/// <param name="context">The database session.</param>
internal sealed class AuditWriter(FushiDbContext context) : IAuditWriter
{
    /// <inheritdoc/>
    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        context.AuditEntries.Add(entry);
    }
}
