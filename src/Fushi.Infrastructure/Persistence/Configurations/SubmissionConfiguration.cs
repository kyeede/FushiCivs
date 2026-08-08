using Fushi.Core.Entities.Submissions;
using Fushi.Infrastructure.Persistence.Converters;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fushi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Submission"/> onto the <c>submissions</c> table.
/// </summary>
internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("submissions");

        builder.HasKey(submission => submission.Id);

        builder.Property(submission => submission.Id)
            .ValueGeneratedNever();

        builder.Property(submission => submission.Code)
            .HasConversion<ShortCodeConverter>()
            .HasMaxLength(6)
            .IsFixedLength()
            .IsRequired();

        builder.Property(submission => submission.Title)
            .HasMaxLength(Submission.MAX_TITLE_LENGTH)
            .IsRequired();

        builder.Property(submission => submission.Content)
            .HasMaxLength(Submission.MAX_CONTENT_LENGTH)
            .IsRequired();

        builder.Property(submission => submission.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(submission => submission.Outcome)
            .HasConversion<int?>();

        // Tally is recomputed from the loaded votes on every read, and Mention is
        // built from the applicant snowflake. Neither is state.
        builder.Ignore(submission => submission.Tally);
        builder.Ignore(submission => submission.IsTerminal);
        builder.Ignore(submission => submission.Mention);

        builder.HasMany(submission => submission.Votes)
            .WithOne()
            .HasForeignKey(vote => vote.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(submission => submission.Votes)
            .HasField("_votes")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(submission => new { submission.GuildId, submission.Code })
            .IsUnique()
            .HasDatabaseName("ix_submissions_guild_code");

        // The guarantee that one intake message becomes at most one submission.
        // Intake re-reads the channel after every restart, so without this a restart
        // would duplicate everything it had already collected.
        builder.HasIndex(submission => new { submission.GuildId, submission.SourceMessageId })
            .IsUnique()
            .HasDatabaseName("ix_submissions_guild_source_message");

        // Serves both the queue read, which filters on status and orders by age, and
        // the paged list, which filters on status and orders by recency.
        builder.HasIndex(submission => new
        {
            submission.GuildId,
            submission.Status,
            submission.CreatedAt,
        })
            .HasDatabaseName("ix_submissions_guild_status_created");

        builder.HasIndex(submission => submission.CycleId)
            .HasDatabaseName("ix_submissions_cycle");

        builder.HasQueryFilter(submission => submission.DeletedAt == null);
    }
}
