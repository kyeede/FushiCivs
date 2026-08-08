using Fushi.Core.Entities.Guilds;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fushi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Guild"/> onto the <c>guilds</c> table.
/// </summary>
/// <remarks>
/// The three value objects a guild carries — its channels, its voting policy, and
/// its schedule — are mapped as complex properties, so each becomes a group of
/// columns on this table rather than a row in a table of its own. That is what
/// they are: a guild has exactly one of each, always, and none of them has an
/// identity worth tracking. Splitting them out would buy three joins to read one
/// configuration.
/// <br/>
/// Their members are listed explicitly rather than left to convention. These are
/// structs whose members are variously <c>init</c>-only, backed by the
/// <c>field</c> keyword, and backed by named private fields, and the mapping is
/// too important to leave to whichever of those conventions happens to fire.
/// </remarks>
internal sealed class GuildConfiguration : IEntityTypeConfiguration<Guild>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Guild> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("guilds");

        builder.HasKey(guild => guild.Id);

        // The key is Discord's guild snowflake. Letting the database generate one
        // would produce a second identity for something that already has a
        // perfectly good one, and every lookup arrives holding the snowflake.
        builder.Property(guild => guild.Id)
            .ValueGeneratedNever();

        builder.ComplexProperty(guild => guild.Channels, channels =>
        {
            channels.Property(value => value.IntakeChannelId).HasColumnName("intake_channel_id");
            channels.Property(value => value.ReviewChannelId).HasColumnName("review_channel_id");
            channels.Property(value => value.ResultsChannelId).HasColumnName("results_channel_id");
            channels.Property(value => value.ArchiveChannelId).HasColumnName("archive_channel_id");
            channels.Property(value => value.LogChannelId).HasColumnName("log_channel_id");
        });

        builder.ComplexProperty(guild => guild.Policy, policy =>
        {
            policy.Property(value => value.ApprovalRatio).HasColumnName("approval_ratio");
            policy.Property(value => value.Quorum).HasColumnName("quorum");
            policy.Property(value => value.AllowAbstain).HasColumnName("allow_abstain");
            policy.Property(value => value.AllowSelfVote).HasColumnName("allow_self_vote");
            policy.Property(value => value.AllowVoteChange).HasColumnName("allow_vote_change");
        });

        builder.ComplexProperty(guild => guild.Schedule, schedule =>
        {
            // Stored as the integer the flags enum already is. The alternative,
            // storing "Monday, Wednesday, Saturday" as text, reads better in a SQL
            // client but breaks the moment a member is renamed.
            schedule.Property(value => value.Days)
                .HasColumnName("cycle_days")
                .HasConversion<int>();

            schedule.Property(value => value.OpensAt).HasColumnName("opens_at");
            schedule.Property(value => value.ClosesAt).HasColumnName("closes_at");

            // An IANA identifier, never a fixed offset, so that the window follows
            // daylight saving instead of drifting an hour twice a year. The longest
            // in the database is comfortably under this.
            schedule.Property(value => value.TimeZoneId)
                .HasColumnName("time_zone_id")
                .HasMaxLength(64)
                .IsRequired();
        });

        builder.Property(guild => guild.IsEnabled)
            .HasDefaultValue(false);

        builder.HasMany(guild => guild.VotingPermissions)
            .WithOne()
            .HasForeignKey(permission => permission.GuildId)
            .OnDelete(DeleteBehavior.Cascade);

        // The collection is exposed as IReadOnlyCollection over a private list, so
        // Entity Framework is pointed at the field. Going through the property
        // would give it something it cannot add to.
        builder.Navigation(guild => guild.VotingPermissions)
            .HasField("_votingPermissions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
