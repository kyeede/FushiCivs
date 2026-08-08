namespace Fushi.Core.Entities.Submissions;

/// <summary>
/// A count of the votes cast on a submission, broken down by choice.
/// </summary>
/// <remarks>
/// A snapshot rather than a live view. Producing one is cheap, so callers
/// recompute instead of caching, which removes any possibility of a stale count
/// being announced as a result.
/// </remarks>
/// <seealso cref="Fushi.Core.Entities.Guilds.VotingPolicy.Evaluate"/>
public readonly record struct VoteTally
{
    /// <summary>
    /// Initialises a tally from its component counts.
    /// </summary>
    /// <param name="approvals">The number of approving votes.</param>
    /// <param name="rejections">The number of rejecting votes.</param>
    /// <param name="abstentions">The number of abstentions.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any count is negative.
    /// </exception>
    public VoteTally(int approvals, int rejections, int abstentions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(approvals);
        ArgumentOutOfRangeException.ThrowIfNegative(rejections);
        ArgumentOutOfRangeException.ThrowIfNegative(abstentions);

        Approvals = approvals;
        Rejections = rejections;
        Abstentions = abstentions;
    }

    /// <summary>
    /// Gets the number of approving votes.
    /// </summary>
    public int Approvals { get; init; }

    /// <summary>
    /// Gets the number of rejecting votes.
    /// </summary>
    public int Rejections { get; init; }

    /// <summary>
    /// Gets the number of abstentions.
    /// </summary>
    public int Abstentions { get; init; }

    /// <summary>
    /// Gets a tally with no votes in it.
    /// </summary>
    public static VoteTally Empty => default;

    /// <summary>
    /// Gets the number of votes that carry a judgement.
    /// </summary>
    /// <value>
    /// Approvals plus rejections. Abstentions are excluded, so this is the
    /// figure quorum is measured against.
    /// </value>
    public int DecidingVotes => Approvals + Rejections;

    /// <summary>
    /// Gets the number of votes cast in total.
    /// </summary>
    /// <value>Approvals, rejections, and abstentions together.</value>
    public int TotalVotes => DecidingVotes + Abstentions;

    /// <summary>
    /// Gets the share of deciding votes that were approvals.
    /// </summary>
    /// <value>
    /// A value from <c>0</c> to <c>1</c>, or <c>0</c> when no deciding votes
    /// were cast. The zero-vote case is reported rather than left undefined so
    /// that a caller never divides by zero; quorum is what distinguishes it
    /// from a genuine unanimous rejection.
    /// </value>
    public double ApprovalRatio => DecidingVotes == 0 ? 0d : Approvals / (double)DecidingVotes;

    /// <summary>
    /// Gets <see cref="ApprovalRatio"/> as a whole-number percentage, for
    /// display.
    /// </summary>
    /// <value>A value from <c>0</c> to <c>100</c>.</value>
    public int ApprovalPercentage => (int)Math.Round(ApprovalRatio * 100d, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Gets a value indicating whether any vote at all has been cast.
    /// </summary>
    public bool IsEmpty => TotalVotes == 0;

    /// <summary>
    /// Counts a sequence of votes into a tally.
    /// </summary>
    /// <param name="votes">The votes to count.</param>
    /// <returns>The resulting tally.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="votes"/> is <see langword="null"/>.
    /// </exception>
    public static VoteTally From(IEnumerable<Vote> votes)
    {
        ArgumentNullException.ThrowIfNull(votes);

        int approvals = 0;
        int rejections = 0;
        int abstentions = 0;

        foreach (Vote vote in votes)
        {
            if (vote.IsDeleted)
            {
                continue;
            }

            switch (vote.Choice)
            {
                case VoteChoice.Approve:
                    approvals++;
                    break;
                case VoteChoice.Reject:
                    rejections++;
                    break;
                case VoteChoice.Abstain:
                    abstentions++;
                    break;
                default:
                    break;
            }
        }

        return new VoteTally(approvals, rejections, abstentions);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Approvals} for, {Rejections} against, {Abstentions} abstained ({ApprovalPercentage}%)";
}
