using Fushi.Core.Entities.Submissions;

namespace Fushi.Core.Entities.Guilds;

/// <summary>
/// The rules that turn a set of votes into a decision.
/// </summary>
/// <remarks>
/// This type owns the answer to "did it pass". Keeping the rule in one place
/// means the number shown in a preview, the number applied when a cycle closes,
/// and the number quoted in the results announcement cannot disagree with each
/// other.
/// <br/>
/// Two independent gates apply. Quorum asks whether enough people voted for the
/// result to mean anything; the ratio asks whether those who did voted in
/// favour. Failing quorum produces
/// <see cref="SubmissionOutcome.Skipped"/> rather than a rejection, because
/// "nobody looked at it" and "the panel said no" are different outcomes and
/// only one of them should count against an applicant.
/// </remarks>
/// <seealso cref="Evaluate"/>
public readonly record struct VotingPolicy
{
    /// <summary>
    /// The share of deciding votes that must be approvals when a guild has not
    /// chosen otherwise.
    /// </summary>
    public const double DEFAULT_APPROVAL_RATIO = 0.60d;

    /// <summary>
    /// The number of deciding votes required for a result to count when a guild
    /// has not chosen otherwise.
    /// </summary>
    public const int DEFAULT_QUORUM = 3;

    private readonly double _approvalRatio;
    private readonly int _quorum;

    /// <summary>
    /// Initialises the voting rules.
    /// </summary>
    /// <param name="approvalRatio">
    /// The share of deciding votes that must be approvals, greater than <c>0</c>
    /// and at most <c>1</c>.
    /// </param>
    /// <param name="quorum">
    /// The minimum number of deciding votes for the result to count. Zero
    /// disables the quorum gate entirely.
    /// </param>
    /// <param name="allowAbstain">
    /// Whether voters may abstain. An abstention is recorded as participation
    /// but is excluded from the ratio.
    /// </param>
    /// <param name="allowSelfVote">
    /// Whether an applicant may vote on their own submission.
    /// </param>
    /// <param name="allowVoteChange">
    /// Whether a voter may change their vote while the cycle is still open.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="approvalRatio"/> is zero, negative, above <c>1</c>, or not
    /// a number, or <paramref name="quorum"/> is negative.
    /// </exception>
    public VotingPolicy(
        double approvalRatio = DEFAULT_APPROVAL_RATIO,
        int quorum = DEFAULT_QUORUM,
        bool allowAbstain = true,
        bool allowSelfVote = false,
        bool allowVoteChange = true)
    {
        if (double.IsNaN(approvalRatio))
        {
            throw new ArgumentOutOfRangeException(
                nameof(approvalRatio),
                approvalRatio,
                "The approval ratio must be a number.");
        }

        // Zero is refused rather than stored. The getter below reads a zero as
        // "never configured" and substitutes the default, so a policy constructed
        // with zero would not survive a round trip — it would silently start
        // enforcing 60% instead of the nothing it was asked for. Refusing it here
        // means the three places that have an opinion on the ratio, this
        // constructor, that getter, and the ConfigureVotingPolicy validator, all
        // agree on the same rule.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(approvalRatio);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(approvalRatio, 1.0d);
        ArgumentOutOfRangeException.ThrowIfNegative(quorum);

        _approvalRatio = approvalRatio;
        _quorum = quorum;
        AllowAbstain = allowAbstain;
        AllowSelfVote = allowSelfVote;
        AllowVoteChange = allowVoteChange;
    }

    /// <summary>
    /// Gets the share of deciding votes that must be approvals for a submission
    /// to pass.
    /// </summary>
    /// <value>
    /// A value greater than <c>0</c> and at most <c>1</c>. A stored zero means the
    /// policy was never constructed — a <c>default(VotingPolicy)</c>, or a value
    /// materialised straight from a row — and reads as
    /// <see cref="DEFAULT_APPROVAL_RATIO"/>. The constructor refuses zero, so that
    /// reading is unambiguous.
    /// </value>
    public double ApprovalRatio => _approvalRatio is 0d ? DEFAULT_APPROVAL_RATIO : _approvalRatio;

    /// <summary>
    /// Gets the minimum number of deciding votes for a result to count.
    /// </summary>
    /// <value>
    /// A non-negative count. Zero means any number of votes, including none,
    /// produces a decision.
    /// </value>
    public int Quorum => _quorum;

    /// <summary>
    /// Gets a value indicating whether voters may abstain.
    /// </summary>
    public bool AllowAbstain { get; init; }

    /// <summary>
    /// Gets a value indicating whether an applicant may vote on their own
    /// submission.
    /// </summary>
    public bool AllowSelfVote { get; init; }

    /// <summary>
    /// Gets a value indicating whether a voter may change their vote before the
    /// cycle closes.
    /// </summary>
    public bool AllowVoteChange { get; init; }

    /// <summary>
    /// Gets the rules applied when a guild has not configured its own: a
    /// three-vote quorum, a 60% approval ratio, abstentions and vote changes
    /// allowed, and self-voting refused.
    /// </summary>
    /// <remarks>
    /// Every argument is passed explicitly, and none of them is redundant. A
    /// struct constructor whose parameters all have defaults is still not a
    /// parameterless constructor, so <c>new()</c> binds to the implicit
    /// zero-initialising one and none of the defaults declared above it apply.
    /// <br/>
    /// The two booleans are the reason this cannot be left to a fallback in the
    /// getters the way <see cref="ApprovalRatio"/> handles it. <c>false</c> is a
    /// legitimate setting for both, so a getter cannot tell "off" apart from
    /// "never set" — and the zero value is the opposite of the documented default
    /// in both cases. Written as <c>new()</c>, this returned a policy with no
    /// quorum at all, which decides a submission on a single vote.
    /// </remarks>
    public static VotingPolicy Default => new(
        DEFAULT_APPROVAL_RATIO,
        DEFAULT_QUORUM,
        allowAbstain: true,
        allowSelfVote: false,
        allowVoteChange: true);

    /// <summary>
    /// Gets <see cref="ApprovalRatio"/> as a whole-number percentage, for
    /// display.
    /// </summary>
    /// <value>A value from <c>0</c> to <c>100</c>.</value>
    public int ApprovalPercentage => (int)Math.Round(ApprovalRatio * 100d, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Applies the rules to a set of votes.
    /// </summary>
    /// <remarks>
    /// Abstentions count towards nothing: they are neither approvals nor
    /// rejections, and they do not count towards quorum. An abstention is a
    /// deliberate statement that the voter read the submission and declined to
    /// judge it, which is worth recording but must not tip a decision.
    /// <br/>
    /// The comparison against <see cref="ApprovalRatio"/> is inclusive, so a
    /// policy of exactly <c>0.5</c> passes a submission on a tie. That is a
    /// choice, not an accident: a guild that wants a strict majority should
    /// configure a ratio above one half.
    /// </remarks>
    /// <param name="tally">The votes cast on a submission.</param>
    /// <returns>
    /// <see cref="SubmissionOutcome.Skipped"/> when the quorum was not reached,
    /// <see cref="SubmissionOutcome.Approved"/> when the approval share met the
    /// threshold, and <see cref="SubmissionOutcome.Rejected"/> otherwise.
    /// </returns>
    public SubmissionOutcome Evaluate(VoteTally tally)
    {
        if (tally.DecidingVotes < Quorum || tally.DecidingVotes == 0)
        {
            return SubmissionOutcome.Skipped;
        }

        return tally.ApprovalRatio >= ApprovalRatio
            ? SubmissionOutcome.Approved
            : SubmissionOutcome.Rejected;
    }

    /// <summary>
    /// Calculates how many further approvals would secure a pass, given the
    /// votes cast so far.
    /// </summary>
    /// <remarks>
    /// Used to render live progress on a review message. The figure assumes no
    /// further rejections arrive, so it is a best case rather than a
    /// prediction.
    /// </remarks>
    /// <param name="tally">The votes cast so far.</param>
    /// <returns>
    /// The number of additional approvals needed, or <c>0</c> when the
    /// submission would already pass.
    /// </returns>
    public int ApprovalsNeeded(VoteTally tally)
    {
        int needed = 0;
        VoteTally projected = tally;

        // Bounded by quorum plus the current vote count: each iteration adds an
        // approval, which strictly increases both the ratio and the deciding
        // count, so the loop cannot run away.
        while (Evaluate(projected) != SubmissionOutcome.Approved)
        {
            needed++;
            projected = projected with { Approvals = projected.Approvals + 1 };

            if (needed > Quorum + tally.DecidingVotes + 1)
            {
                break;
            }
        }

        return needed;
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"{ApprovalPercentage}% of at least {Quorum} vote(s)";
}
