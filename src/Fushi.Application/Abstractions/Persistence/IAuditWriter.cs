using Fushi.Core.Entities.Audits;

namespace Fushi.Application.Abstractions.Persistence;

/// <summary>
/// Appends entries to a guild's audit trail.
/// </summary>
/// <remarks>
/// Write-only by design. Reading the trail is a query with its own repository
/// method; giving the writer no read surface means an audit entry cannot be
/// fetched, modified, and saved back, which is the only way it could be
/// falsified through the application.
/// <br/>
/// Entries are staged rather than written immediately, so they commit in the same
/// transaction as the change they describe. An audit entry therefore cannot
/// survive a rolled-back command, and a committed change cannot lack one.
/// </remarks>
public interface IAuditWriter
{
    /// <summary>
    /// Stages an audit entry for insertion.
    /// </summary>
    /// <param name="entry">The entry to record.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entry"/> is <see langword="null"/>.
    /// </exception>
    void Record(AuditEntry entry);
}
