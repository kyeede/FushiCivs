using Fushi.Core.Entities.Guilds;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fushi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="VotingPermission"/> onto the <c>voting_permissions</c> table.
/// </summary>
internal sealed class VotingPermissionConfiguration
    : IEntityTypeConfiguration<VotingPermission>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<VotingPermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("voting_permissions");

        builder.HasKey(permission => permission.Id);

        builder.Property(permission => permission.Id)
            .ValueGeneratedNever();

        builder.Property(permission => permission.Scope)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(permission => permission.Note)
            .HasMaxLength(512);

        // A guild should not end up with two live grants for the same target, which
        // would have to be revoked twice to take effect. The index is filtered on the
        // soft-delete column so that revoking and re-granting stays possible.
        builder.HasIndex(
                permission => new
                {
                    permission.GuildId,
                    permission.Scope,
                    permission.TargetId,
                })
            .HasFilter("deleted_at IS NULL")
            .IsUnique()
            .HasDatabaseName("ix_voting_permissions_guild_target_live");
    }
}
