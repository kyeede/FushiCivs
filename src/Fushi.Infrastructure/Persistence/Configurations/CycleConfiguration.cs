using Fushi.Core.Entities.Cycles;
using Fushi.Infrastructure.Persistence.Converters;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fushi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Cycle"/> onto the <c>cycles</c> table.
/// </summary>
/// <remarks>
/// The policy columns here are a copy of the guild's rules as they stood when the
/// cycle was created, not a reference to them. Duplicating five columns per cycle
/// is the price of being able to explain a result years later, when the guild's
/// current threshold is no longer the one the vote was judged against.
/// </remarks>
internal sealed class CycleConfiguration : IEntityTypeConfiguration<Cycle>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Cycle> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("cycles");

        builder.HasKey(cycle => cycle.Id);

        builder.Property(cycle => cycle.Id)
            .ValueGeneratedNever();

        builder.Property(cycle => cycle.Code)
            .HasConversion<ShortCodeConverter>()
            .HasMaxLength(6)
            .IsFixedLength()
            .IsRequired();

        builder.Property(cycle => cycle.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.ComplexProperty(cycle => cycle.Policy, policy =>
        {
            policy.Property(value => value.ApprovalRatio).HasColumnName("approval_ratio");
            policy.Property(value => value.Quorum).HasColumnName("quorum");
            policy.Property(value => value.AllowAbstain).HasColumnName("allow_abstain");
            policy.Property(value => value.AllowSelfVote).HasColumnName("allow_self_vote");
            policy.Property(value => value.AllowVoteChange).HasColumnName("allow_vote_change");
        });

        // Window, IsTerminal, and IsAcceptingVotes are all computed from the three
        // stored columns. Window in particular would otherwise be mapped as a second
        // copy of the date and both instants.
        builder.Ignore(cycle => cycle.Window);
        builder.Ignore(cycle => cycle.IsTerminal);

        builder.HasMany(cycle => cycle.Submissions)
            .WithOne()
            .HasForeignKey(submission => submission.CycleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Navigation(cycle => cycle.Submissions)
            .HasField("_submissions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(cycle => new { cycle.GuildId, cycle.Code })
            .IsUnique()
            .HasDatabaseName("ix_cycles_guild_code");

        // A guild runs at most one cycle per scheduled date. This is what makes the
        // scheduler idempotent: a second pass over the same day loses the race rather
        // than creating a duplicate.
        builder.HasIndex(cycle => new { cycle.GuildId, cycle.ScheduledDate })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_cycles_guild_date");

        // At most one open cycle per guild, enforced by the database rather than by a
        // check in the handler, because two concurrent open commands would both pass
        // a check and only one can win an index.
        builder.HasIndex(cycle => cycle.GuildId)
            .IsUnique()
            .HasFilter($"status = {(int)CycleStatus.Open} AND deleted_at IS NULL")
            .HasDatabaseName("ix_cycles_one_open_per_guild");
    }
}
