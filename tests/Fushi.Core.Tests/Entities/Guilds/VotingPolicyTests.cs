using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;

namespace Fushi.Core.Tests.Entities.Guilds;

/// <summary>
/// Covers <see cref="VotingPolicy"/>: the two independent gates that turn a set
/// of votes into an outcome, the inclusive ratio boundary, the treatment of
/// abstentions, the approvals-still-needed projection, and construction
/// validation.
/// </summary>
public sealed class VotingPolicyTests
{
    // The distinction a well-meaning refactor is most likely to lose. Two
    // approvals and no rejections is unanimous, and it is still not a decision,
    // because "nobody looked at it" must not count against an applicant the way a
    // rejection does.
    [Fact]
    public void FailingQuorumIsSkippedRatherThanApprovedHoweverFavourableTheVotes()
    {
        SubmissionOutcome outcome = Policy().Evaluate(new VoteTally(approvals: 2, rejections: 0, abstentions: 0));

        outcome.ShouldBe(SubmissionOutcome.Skipped);
        outcome.ShouldNotBe(SubmissionOutcome.Approved);
    }

    [Fact]
    public void FailingQuorumIsSkippedRatherThanRejectedHoweverUnfavourableTheVotes()
    {
        Policy()
            .Evaluate(new VoteTally(approvals: 0, rejections: 2, abstentions: 0))
            .ShouldBe(SubmissionOutcome.Skipped);
    }

    [Fact]
    public void NoVotesAtAllIsSkipped()
    {
        Policy().Evaluate(VoteTally.Empty).ShouldBe(SubmissionOutcome.Skipped);
    }

    // Even with the quorum gate disabled, an empty tally has nothing to decide on.
    [Fact]
    public void NoVotesAtAllIsStillSkippedWhenTheQuorumGateIsDisabled()
    {
        Policy(quorum: 0).Evaluate(VoteTally.Empty).ShouldBe(SubmissionOutcome.Skipped);
    }

    [Theory]
    [InlineData(3, 0, SubmissionOutcome.Approved)]
    [InlineData(4, 1, SubmissionOutcome.Approved)]
    [InlineData(2, 1, SubmissionOutcome.Approved)]
    [InlineData(2, 2, SubmissionOutcome.Rejected)]
    [InlineData(1, 2, SubmissionOutcome.Rejected)]
    [InlineData(0, 3, SubmissionOutcome.Rejected)]
    public void MeetingQuorumDecidesTheSubmissionOnTheApprovalShare(
        int approvals,
        int rejections,
        SubmissionOutcome expected)
    {
        Policy().Evaluate(new VoteTally(approvals, rejections, abstentions: 0)).ShouldBe(expected);
    }

    // The comparison is inclusive, so exactly meeting the threshold passes. Three
    // of five is 0.60 against a 0.60 policy, and a guild wanting a strict
    // majority is expected to configure a ratio above it.
    [Fact]
    public void ExactlyMeetingTheApprovalRatioPasses()
    {
        VoteTally onTheLine = new(approvals: 3, rejections: 2, abstentions: 0);

        onTheLine.ApprovalRatio.ShouldBe(0.60d);
        Policy().Evaluate(onTheLine).ShouldBe(SubmissionOutcome.Approved);
    }

    [Fact]
    public void FallingOneVoteShortOfTheApprovalRatioFails()
    {
        Policy()
            .Evaluate(new VoteTally(approvals: 2, rejections: 2, abstentions: 0))
            .ShouldBe(SubmissionOutcome.Rejected);
    }

    // Abstentions are participation, not judgement: they neither reach the quorum
    // nor move the ratio.
    [Fact]
    public void AbstentionsCountTowardsNeitherQuorumNorTheRatio()
    {
        VoteTally withoutAbstentions = new(approvals: 3, rejections: 2, abstentions: 0);
        VoteTally withAbstentions = withoutAbstentions with { Abstentions = 20 };

        Policy().Evaluate(withAbstentions).ShouldBe(Policy().Evaluate(withoutAbstentions));
        withAbstentions.ApprovalRatio.ShouldBe(withoutAbstentions.ApprovalRatio);
        withAbstentions.DecidingVotes.ShouldBe(withoutAbstentions.DecidingVotes);
    }

    [Fact]
    public void AbstentionsAloneCannotReachQuorum()
    {
        Policy()
            .Evaluate(new VoteTally(approvals: 0, rejections: 0, abstentions: 50))
            .ShouldBe(SubmissionOutcome.Skipped);
    }

    [Fact]
    public void ApprovalsNeededIsZeroWhenTheSubmissionWouldAlreadyPass()
    {
        Policy().ApprovalsNeeded(new VoteTally(approvals: 3, rejections: 2, abstentions: 0)).ShouldBe(0);
    }

    // The projection and the verdict have to be the same rule seen from two
    // sides: adding exactly the number of approvals it reports must flip the
    // outcome to approved, and one fewer must not.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    public void ApprovalsNeededAgreesWithEvaluateAtTheBoundary(int approvals, int rejections)
    {
        VotingPolicy policy = Policy();
        VoteTally tally = new(approvals, rejections, abstentions: 0);

        int needed = policy.ApprovalsNeeded(tally);

        policy.Evaluate(tally with { Approvals = approvals + needed }).ShouldBe(SubmissionOutcome.Approved);

        if (needed > 0)
        {
            policy
                .Evaluate(tally with { Approvals = approvals + needed - 1 })
                .ShouldNotBe(SubmissionOutcome.Approved);
        }
    }

    [Fact]
    public void ApprovalPercentageRendersTheRatioForDisplay()
    {
        Policy().ApprovalPercentage.ShouldBe(60);
        Policy(approvalRatio: 0.5d).ApprovalPercentage.ShouldBe(50);
        Policy(approvalRatio: 1.0d).ApprovalPercentage.ShouldBe(100);
    }

    // Zero is a legitimate quorum, meaning "no quorum gate", so a single
    // deciding vote settles it.
    [Fact]
    public void AQuorumOfZeroLetsASingleVoteDecide()
    {
        VotingPolicy policy = Policy(quorum: 0);

        policy.Evaluate(new VoteTally(1, 0, 0)).ShouldBe(SubmissionOutcome.Approved);
        policy.Evaluate(new VoteTally(0, 1, 0)).ShouldBe(SubmissionOutcome.Rejected);
    }

    [Theory]
    [InlineData(-0.01d)]
    [InlineData(1.01d)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void ConstructionRejectsAnApprovalRatioOutsideZeroToOne(double approvalRatio)
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(() => new VotingPolicy(approvalRatio));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ConstructionRejectsANegativeQuorum(int quorum)
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(() => new VotingPolicy(quorum: quorum));
    }

    [Theory]
    [InlineData(0.01d)]
    [InlineData(0.5d)]
    [InlineData(1.0d)]
    public void ConstructionAcceptsTheEndsOfTheApprovalRange(double approvalRatio)
    {
        new VotingPolicy(approvalRatio).ApprovalRatio.ShouldBe(approvalRatio);
    }

    // Zero is refused rather than stored. The getter reads a zero as "never
    // configured" and substitutes the default, so accepting zero would mean a
    // policy silently enforcing 60% after being asked for nothing. Refusing it
    // keeps the constructor, the getter, and the ConfigureVotingPolicy validator
    // agreeing on one rule.
    [Fact]
    public void AnApprovalRatioOfZeroIsRefusedRatherThanSilentlyBecomingTheDefault()
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(() => new VotingPolicy(approvalRatio: 0.0d));
    }

    // The reading the constructor's refusal protects: a zero in the backing field
    // can only have come from a value that never ran the constructor.
    [Fact]
    public void APolicyBuiltWithoutItsConstructorReadsTheDocumentedRatio()
    {
        VotingPolicy materialised = default;

        materialised.ApprovalRatio.ShouldBe(VotingPolicy.DEFAULT_APPROVAL_RATIO);
    }

    private static VotingPolicy Policy(
        double approvalRatio = VotingPolicy.DEFAULT_APPROVAL_RATIO,
        int quorum = VotingPolicy.DEFAULT_QUORUM)
        => new(approvalRatio, quorum);
}
