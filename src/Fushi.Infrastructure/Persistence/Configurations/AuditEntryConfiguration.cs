using Fushi.Core.Entities.Audits;
using Fushi.Infrastructure.Persistence.Converters;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fushi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="AuditEntry"/> onto the <c>audit_entries</c> table.
/// </summary>
/// <remarks>
/// The only table with no soft-delete column and no query filter. An audit entry is
/// written once and never changed or removed; pruning old ones is an operational
/// task carried out against the table directly, so there is nothing for the
/// application to express.
/// </remarks>
internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_entries");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .ValueGeneratedNever();

        builder.Property(entry => entry.Scope)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(entry => entry.Action)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(entry => entry.SubjectCode)
            .HasConversion<NullableShortCodeConverter>()
            .HasMaxLength(6);

        builder.Property(entry => entry.Reason)
            .HasMaxLength(AuditEntry.MAX_REASON_LENGTH);

        // jsonb rather than text, so that a question nobody anticipated can still be
        // answered with a query rather than by exporting the column and parsing it
        // elsewhere. It costs a parse on write and nothing on read.
        builder.Property(entry => entry.Metadata)
            .HasColumnType("jsonb");

        builder.Ignore(entry => entry.IsAutomated);

        // The trail is read newest-first, per guild, sometimes narrowed to one area.
        // Descending on the timestamp lets the common read walk the index backwards
        // without a sort.
        builder.HasIndex(entry => new { entry.GuildId, entry.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_audit_entries_guild_created");

        builder.HasIndex(entry => new { entry.GuildId, entry.Scope, entry.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_audit_entries_guild_scope_created");

        // "What happened to submission 7K4M2P" is the question this trail exists to
        // answer, and it arrives holding a code rather than an identifier.
        builder.HasIndex(entry => new { entry.GuildId, entry.SubjectCode })
            .HasDatabaseName("ix_audit_entries_guild_subject_code");
    }
}
