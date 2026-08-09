using Fushi.Core.Entities.Submissions;

namespace Fushi.Core.Tests.Entities.Submissions;

/// <summary>
/// Covers <see cref="Vote"/>: revising a decision in place rather than
/// inserting a second row, the comment rules, and which choices count towards
/// a quorum.
/// </summary>
public sealed class VoteTests
{
    private static readonly DateTimeOffset Cast = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewVoteCarriesItsChoiceAndHasNeverBeenRevised()
    {
        Vote vote = New(VoteChoice.Approve);

        vote.Choice.ShouldBe(VoteChoice.Approve);
        vote.VoterId.ShouldBe(9uL);
        vote.RevisionCount.ShouldBe(0);
        vote.Comment.ShouldBeNull();
        vote.CreatedAt.ShouldBe(Cast);
    }

    // The row answers "how does this person stand now", and that has exactly one
    // answer at a time, so a change overwrites rather than accumulates.
    [Fact]
    public void RevisingOverwritesTheDecisionInPlace()
    {
        Vote vote = New(VoteChoice.Approve);

        vote.Revise(VoteChoice.Reject, Cast.AddHours(1), "On reflection.").ShouldBeTrue();

        vote.Choice.ShouldBe(VoteChoice.Reject);
        vote.Comment.ShouldBe("On reflection.");
        vote.RevisionCount.ShouldBe(1);
        vote.UpdatedAt.ShouldBe(Cast.AddHours(1));
        vote.UpdatedBy.ShouldBe(9uL);
    }

    // Voting the same way again is not a revision, so a double-click does not
    // show up in the results summary as "changed their mind".
    [Fact]
    public void RevisingToTheSameDecisionChangesNothing()
    {
        Vote vote = New(VoteChoice.Approve);

        vote.Revise(VoteChoice.Approve, Cast.AddHours(1)).ShouldBeFalse();

        vote.RevisionCount.ShouldBe(0);
        vote.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public void ChangingOnlyTheCommentStillCountsAsARevision()
    {
        Vote vote = New(VoteChoice.Approve);

        vote.Revise(VoteChoice.Approve, Cast.AddHours(1), "Adding my reasoning.").ShouldBeTrue();

        vote.RevisionCount.ShouldBe(1);
    }

    [Fact]
    public void ACommentIsTrimmedAndABlankOneIsRecordedAsAbsent()
    {
        var vote = Vote.Create(Guid.NewGuid(), 9uL, VoteChoice.Approve, Cast, "  Looks good.  ");

        vote.Comment.ShouldBe("Looks good.");

        _ = vote.Revise(VoteChoice.Approve, Cast.AddHours(1), "   ");

        vote.Comment.ShouldBeNull();
    }

    [Fact]
    public void ACommentThatIsTooLongIsRefused()
    {
        string overlong = new('c', Vote.MAX_COMMENT_LENGTH + 1);

        _ = Should.Throw<ArgumentOutOfRangeException>(
            () => Vote.Create(Guid.NewGuid(), 9uL, VoteChoice.Approve, Cast, overlong));

        _ = Should.Throw<ArgumentOutOfRangeException>(
            () => New(VoteChoice.Approve).Revise(VoteChoice.Approve, Cast.AddHours(1), overlong));
    }

    // Abstentions are participation, not judgement, so they are excluded from the
    // count quorum is measured against.
    [Theory]
    [InlineData(VoteChoice.Approve, true)]
    [InlineData(VoteChoice.Reject, true)]
    [InlineData(VoteChoice.Abstain, false)]
    public void OnlyApprovalsAndRejectionsCarryAJudgement(VoteChoice choice, bool expected) => New(choice).IsDeciding.ShouldBe(expected);

    [Fact]
    public void AVoteMentionsWhoeverCastIt() => New(VoteChoice.Approve).Mention.ShouldBe("<@9>");

    [Fact]
    public void AVoteNeedsASubmissionAndARealVoter()
    {
        _ = Should.Throw<ArgumentException>(
            () => Vote.Create(Guid.Empty, 9uL, VoteChoice.Approve, Cast));

        _ = Should.Throw<ArgumentOutOfRangeException>(
            () => Vote.Create(Guid.NewGuid(), 0uL, VoteChoice.Approve, Cast));
    }

    [Fact]
    public void AnUndefinedChoiceIsRefused()
    {
        _ = Should.Throw<ArgumentException>(
            () => Vote.Create(Guid.NewGuid(), 9uL, (VoteChoice)99, Cast));

        _ = Should.Throw<ArgumentException>(
            () => New(VoteChoice.Approve).Revise((VoteChoice)99, Cast.AddHours(1)));
    }

    private static Vote New(VoteChoice choice) => Vote.Create(Guid.NewGuid(), 9uL, choice, Cast);
}
