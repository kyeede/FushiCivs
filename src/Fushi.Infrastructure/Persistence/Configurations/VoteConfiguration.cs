using Fushi.Core.Entities.Submissions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fushi.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Vote"/> onto the <c>votes</c> table.
/// </summary>
/// <remarks>
/// Changing a vote overwrites the row rather than adding another, so this table
/// holds one row per voter per submission and a count is a straightforward
/// aggregate. The history of who changed their mind lives in the audit trail,
/// which is the right place for it.
/// </remarks>
internal sealed class VoteConfiguration : IEntityTypeConfiguration<Vote>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Vote> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("votes");

        builder.HasKey(vote => vote.Id);

        builder.Property(vote => vote.Id)
            .ValueGeneratedNever();

        builder.Property(vote => vote.Choice)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(vote => vote.Comment)
            .HasMaxLength(Vote.MAX_COMMENT_LENGTH);

        builder.Property(vote => vote.RevisionCount)
            .HasDefaultValue(0);

        builder.Ignore(vote => vote.IsDeciding);

        // One live vote per person per submission. The entity already refuses to add
        // a second, but that only holds within a single loaded instance; two requests
        // racing would each see no existing vote. This is what actually decides it.
        builder.HasIndex(vote => new { vote.SubmissionId, vote.VoterId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_votes_submission_voter_live");
    }
}
