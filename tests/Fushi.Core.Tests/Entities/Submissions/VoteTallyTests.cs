using Fushi.Core.Entities.Submissions;

namespace Fushi.Core.Tests.Entities.Submissions;

/// <summary>
/// Covers <see cref="VoteTally"/>: the derived counts, the approval share and
/// its percentage, counting a collection of votes, and the empty case.
/// </summary>
public sealed class VoteTallyTests
{
    [Fact]
    public void DecidingVotesExcludeAbstentionsWhileTotalVotesIncludeThem()
    {
        VoteTally tally = new(approvals: 4, rejections: 3, abstentions: 2);

        tally.DecidingVotes.ShouldBe(7);
        tally.TotalVotes.ShouldBe(9);
        tally.IsEmpty.ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, 1, 0.5d, 50)]
    [InlineData(3, 2, 0.6d, 60)]
    [InlineData(1, 2, 1d / 3d, 33)]
    [InlineData(5, 3, 0.625d, 63)]
    [InlineData(4, 0, 1.0d, 100)]
    [InlineData(0, 4, 0.0d, 0)]
    public void TheApprovalShareIsTheApprovalsOverTheDecidingVotes(
        int approvals,
        int rejections,
        double expectedRatio,
        int expectedPercentage)
    {
        VoteTally tally = new(approvals, rejections, abstentions: 7);

        tally.ApprovalRatio.ShouldBe(expectedRatio, tolerance: 1e-9);
        tally.ApprovalPercentage.ShouldBe(expectedPercentage);
    }

    // Reported as zero rather than left undefined, so no caller ever divides by
    // zero. Quorum is what distinguishes this from a genuine unanimous rejection.
    [Fact]
    public void AnEmptyTallyReportsAZeroShareInsteadOfDividingByZero()
    {
        VoteTally empty = VoteTally.Empty;

        empty.DecidingVotes.ShouldBe(0);
        empty.TotalVotes.ShouldBe(0);
        empty.ApprovalRatio.ShouldBe(0d);
        empty.ApprovalPercentage.ShouldBe(0);
        empty.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void ATallyOfAbstentionsAloneIsNotEmptyButHasNoDecidingVotes()
    {
        VoteTally tally = new(approvals: 0, rejections: 0, abstentions: 3);

        tally.IsEmpty.ShouldBeFalse();
        tally.DecidingVotes.ShouldBe(0);
        tally.ApprovalRatio.ShouldBe(0d);
    }

    [Fact]
    public void CountingASequenceOfVotesGroupsThemByChoice()
    {
        List<Vote> votes =
        [
            Cast(1uL, VoteChoice.Approve),
            Cast(2uL, VoteChoice.Approve),
            Cast(3uL, VoteChoice.Reject),
            Cast(4uL, VoteChoice.Abstain),
        ];

        VoteTally tally = VoteTally.From(votes);

        tally.Approvals.ShouldBe(2);
        tally.Rejections.ShouldBe(1);
        tally.Abstentions.ShouldBe(1);
    }

    // Votes are soft-deleted rather than removed so an outcome stays explainable,
    // which means the count has to skip them or a retracted vote would still
    // decide the submission.
    [Fact]
    public void CountingSkipsVotesThatHaveBeenRetracted()
    {
        Vote retracted = Cast(1uL, VoteChoice.Approve);
        retracted.MarkDeleted(Moment.AddHours(1), deletedBy: 1uL);

        VoteTally tally = VoteTally.From([retracted, Cast(2uL, VoteChoice.Reject)]);

        tally.Approvals.ShouldBe(0);
        tally.Rejections.ShouldBe(1);
        tally.TotalVotes.ShouldBe(1);
    }

    [Fact]
    public void CountingAnEmptySequenceProducesTheEmptyTally()
    {
        VoteTally.From([]).ShouldBe(VoteTally.Empty);
    }

    [Fact]
    public void CountingRejectsANullSequence()
    {
        _ = Should.Throw<ArgumentNullException>(() => VoteTally.From(null!));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void ConstructionRejectsANegativeCount(int approvals, int rejections, int abstentions)
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(() => new VoteTally(approvals, rejections, abstentions));
    }

    private static DateTimeOffset Moment => new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static Vote Cast(ulong voterId, VoteChoice choice)
        => Vote.Create(Guid.CreateVersion7(Moment), voterId, choice, Moment);
}
