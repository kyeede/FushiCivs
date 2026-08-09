using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;

namespace Fushi.Core.Tests.Entities.Submissions;

/// <summary>
/// Covers <see cref="Submission"/>: the states it may move between, the
/// guarantee that one voter never holds two live votes, and the clearing of
/// votes when a submission goes back to the queue.
/// </summary>
public sealed class SubmissionTests
{
    private static readonly DateTimeOffset Captured = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewSubmissionStartsAsADraftWithNoVotes()
    {
        Submission submission = New();

        submission.Status.ShouldBe(SubmissionStatus.Draft);
        submission.CycleId.ShouldBeNull();
        submission.Outcome.ShouldBeNull();
        submission.Votes.ShouldBeEmpty();
        submission.Tally.IsEmpty.ShouldBeTrue();
        submission.IsTerminal.ShouldBeFalse();
        submission.Code.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void ADraftMovesThroughTheQueueIntoReviewAndOnToADecision()
    {
        Submission submission = New();
        var cycleId = Guid.NewGuid();

        submission.Queue(Captured.AddMinutes(1), 7uL);
        submission.PutUnderReview(cycleId, Captured.AddMinutes(2), 7uL);
        submission.Decide(SubmissionOutcome.Approved, Captured.AddMinutes(3), 7uL);

        submission.Status.ShouldBe(SubmissionStatus.Decided);
        submission.CycleId.ShouldBe(cycleId);
        submission.Outcome.ShouldBe(SubmissionOutcome.Approved);
        submission.DecidedAt.ShouldBe(Captured.AddMinutes(3));
        submission.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void OnlyADraftCanBeQueued()
    {
        Submission submission = Queued();

        _ = Should.Throw<InvalidOperationException>(() => submission.Queue(Captured.AddMinutes(2), 7uL));
    }

    [Fact]
    public void OnlyAQueuedSubmissionCanBePutUnderReview()
    {
        Submission draft = New();

        _ = Should.Throw<InvalidOperationException>(
            () => draft.PutUnderReview(Guid.NewGuid(), Captured.AddMinutes(1), 7uL));
    }

    [Fact]
    public void AReviewNeedsARealCycle()
    {
        Submission submission = Queued();

        _ = Should.Throw<ArgumentException>(
            () => submission.PutUnderReview(Guid.Empty, Captured.AddMinutes(2), 7uL));
    }

    [Theory]
    [InlineData(SubmissionStatus.Draft)]
    [InlineData(SubmissionStatus.Queued)]
    [InlineData(SubmissionStatus.Decided)]
    [InlineData(SubmissionStatus.Withdrawn)]
    public void VotesCanOnlyBeCastOnASubmissionThatIsUnderReview(SubmissionStatus status)
    {
        Submission submission = At(status);

        _ = Should.Throw<InvalidOperationException>(
            () => submission.RecordVote(9uL, VoteChoice.Approve, Captured.AddHours(1)));
    }

    // One voter, one live vote. Voting again revises what is already there, so a
    // change of mind cannot quietly count twice.
    [Fact]
    public void VotingTwiceRevisesTheExistingVoteRatherThanAddingASecond()
    {
        Submission submission = UnderReview();

        Vote first = submission.RecordVote(9uL, VoteChoice.Approve, Captured.AddHours(1));
        Vote second = submission.RecordVote(9uL, VoteChoice.Reject, Captured.AddHours(2), "Changed my mind.");

        second.ShouldBeSameAs(first);
        submission.Votes.Count.ShouldBe(1);
        second.Choice.ShouldBe(VoteChoice.Reject);
        second.RevisionCount.ShouldBe(1);
        submission.Tally.ShouldBe(new VoteTally(approvals: 0, rejections: 1, abstentions: 0));
    }

    [Fact]
    public void DifferentVotersEachGetTheirOwnVote()
    {
        Submission submission = UnderReview();

        _ = submission.RecordVote(9uL, VoteChoice.Approve, Captured.AddHours(1));
        _ = submission.RecordVote(10uL, VoteChoice.Reject, Captured.AddHours(1));
        _ = submission.RecordVote(11uL, VoteChoice.Abstain, Captured.AddHours(1));

        submission.Votes.Count.ShouldBe(3);
        submission.Tally.ShouldBe(new VoteTally(approvals: 1, rejections: 1, abstentions: 1));
    }

    [Fact]
    public void RetractingAVoteRemovesItFromTheTallyWithoutRemovingTheRow()
    {
        Submission submission = UnderReview();
        _ = submission.RecordVote(9uL, VoteChoice.Approve, Captured.AddHours(1));

        submission.RetractVote(9uL, Captured.AddHours(2)).ShouldBeTrue();

        submission.Tally.IsEmpty.ShouldBeTrue();
        submission.Votes.Count.ShouldBe(1);
        submission.FindVote(9uL).ShouldBeNull();
    }

    [Fact]
    public void RetractingAVoteNobodyCastReportsThatNothingHappened()
    {
        Submission submission = UnderReview();

        submission.RetractVote(9uL, Captured.AddHours(2)).ShouldBeFalse();
    }

    // A retracted vote leaves the voter free to vote again, and doing so must
    // produce a fresh vote rather than resurrect the retracted one.
    [Fact]
    public void AVoterWhoRetractedCanVoteAgain()
    {
        Submission submission = UnderReview();
        _ = submission.RecordVote(9uL, VoteChoice.Approve, Captured.AddHours(1));
        _ = submission.RetractVote(9uL, Captured.AddHours(2));

        Vote replacement = submission.RecordVote(9uL, VoteChoice.Reject, Captured.AddHours(3));

        replacement.IsDeleted.ShouldBeFalse();
        submission.Tally.ShouldBe(new VoteTally(approvals: 0, rejections: 1, abstentions: 0));
    }

    // The cycle those votes were cast under no longer counts, so carrying them
    // into the next one would let a voter's decision apply twice.
    [Fact]
    public void ReturningToTheQueueDetachesTheCycleAndClearsTheVotes()
    {
        Submission submission = UnderReview();
        _ = submission.RecordVote(9uL, VoteChoice.Approve, Captured.AddHours(1));
        _ = submission.RecordVote(10uL, VoteChoice.Approve, Captured.AddHours(1));

        submission.ReturnToQueue(Captured.AddHours(2), 7uL);

        submission.Status.ShouldBe(SubmissionStatus.Queued);
        submission.CycleId.ShouldBeNull();
        submission.Tally.IsEmpty.ShouldBeTrue();
        submission.Votes.ShouldAllBe(vote => vote.IsDeleted);
    }

    [Fact]
    public void ADecidedSubmissionCannotBeRequeuedEditedOrWithdrawn()
    {
        Submission submission = Decided();

        _ = Should.Throw<InvalidOperationException>(() => submission.ReturnToQueue(Captured.AddDays(1), 7uL));
        _ = Should.Throw<InvalidOperationException>(
            () => submission.Revise("New title", "New content.", Captured.AddDays(1), 7uL));
        _ = Should.Throw<InvalidOperationException>(() => submission.Withdraw(Captured.AddDays(1), 7uL));
        _ = Should.Throw<InvalidOperationException>(
            () => submission.Decide(SubmissionOutcome.Rejected, Captured.AddDays(1), 7uL));
    }

    [Fact]
    public void AWithdrawnSubmissionIsTerminalToo()
    {
        Submission submission = Queued();

        submission.Withdraw(Captured.AddHours(1), 7uL);

        submission.Status.ShouldBe(SubmissionStatus.Withdrawn);
        submission.IsTerminal.ShouldBeTrue();
        _ = Should.Throw<InvalidOperationException>(() => submission.Withdraw(Captured.AddHours(2), 7uL));
    }

    [Fact]
    public void ARevisionTrimsAndStampsTheChange()
    {
        Submission submission = New();

        submission.Revise("  Trimmed title  ", "  Trimmed content.  ", Captured.AddHours(1), 7uL);

        submission.Title.ShouldBe("Trimmed title");
        submission.Content.ShouldBe("Trimmed content.");
        submission.UpdatedAt.ShouldBe(Captured.AddHours(1));
    }

    [Fact]
    public void ATitleOrBodyThatIsTooLongIsRefused()
    {
        Submission submission = New();

        _ = Should.Throw<ArgumentOutOfRangeException>(() => submission.Revise(
            new string('t', Submission.MAX_TITLE_LENGTH + 1),
            "Fine.",
            Captured.AddHours(1),
            7uL));

        _ = Should.Throw<ArgumentOutOfRangeException>(() => submission.Revise(
            "Fine",
            new string('c', Submission.MAX_CONTENT_LENGTH + 1),
            Captured.AddHours(1),
            7uL));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ATitleOrBodyThatSaysNothingIsRefused(string blank)
    {
        _ = Should.Throw<ArgumentException>(() => New().Revise(blank, "Fine.", Captured.AddHours(1), 7uL));
        _ = Should.Throw<ArgumentException>(() => New().Revise("Fine", blank, Captured.AddHours(1), 7uL));
    }

    [Fact]
    public void AnUndefinedOutcomeIsRefused()
    {
        Submission submission = UnderReview();

        _ = Should.Throw<ArgumentException>(
            () => submission.Decide((SubmissionOutcome)99, Captured.AddHours(1), 7uL));
    }

    // A code goes out to voters the moment the review message is posted, and
    // people write it down, so it stops being reassignable at that point.
    [Fact]
    public void ACodeCanBeReplacedUntilTheSubmissionHasBeenPublished()
    {
        Submission submission = New();
        var replacement = ShortCode.New();

        submission.ReassignCode(replacement);
        submission.Code.ShouldBe(replacement);

        submission.SetReviewMessage(11uL, threadId: null, Captured.AddHours(1), 7uL);

        _ = Should.Throw<InvalidOperationException>(() => submission.ReassignCode(ShortCode.New()));
        _ = Should.Throw<ArgumentException>(() => submission.ReassignCode(ShortCode.Empty));
    }

    [Fact]
    public void AThreadSnowflakeOfZeroIsRecordedAsNoThreadAtAll()
    {
        Submission submission = New();

        submission.SetReviewMessage(11uL, threadId: 0uL, Captured.AddHours(1), 7uL);

        submission.ThreadId.ShouldBeNull();
        submission.ReviewMessageId.ShouldBe(11uL);
    }

    [Fact]
    public void EvaluatingAppliesAPolicyWithoutChangingTheSubmission()
    {
        Submission submission = UnderReview();
        _ = submission.RecordVote(9uL, VoteChoice.Approve, Captured.AddHours(1));
        _ = submission.RecordVote(10uL, VoteChoice.Approve, Captured.AddHours(1));

        submission.Evaluate(VotingPolicy.Default).ShouldBe(SubmissionOutcome.Skipped);

        submission.Status.ShouldBe(SubmissionStatus.UnderReview);
        submission.Outcome.ShouldBeNull();
    }

    [Fact]
    public void TheApplicantIsWhoTheSubmissionMentions() => New().Mention.ShouldBe("<@5>");

    [Theory]
    [InlineData(0uL, 5uL, 6uL, 7uL)]
    [InlineData(1uL, 0uL, 6uL, 7uL)]
    [InlineData(1uL, 5uL, 0uL, 7uL)]
    [InlineData(1uL, 5uL, 6uL, 0uL)]
    public void EverySnowflakeASubmissionCarriesMustBeReal(
        ulong guildId,
        ulong applicantId,
        ulong channelId,
        ulong messageId)
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(() => Submission.Create(
            guildId,
            applicantId,
            channelId,
            messageId,
            "A title",
            "Some content.",
            Captured));
    }

    private static Submission New()
        => Submission.Create(1uL, 5uL, 6uL, 7uL, "A title", "Some content.", Captured);

    private static Submission Queued()
    {
        Submission submission = New();
        submission.Queue(Captured.AddMinutes(1), 7uL);

        return submission;
    }

    private static Submission UnderReview()
    {
        Submission submission = Queued();
        submission.PutUnderReview(Guid.NewGuid(), Captured.AddMinutes(2), 7uL);

        return submission;
    }

    private static Submission Decided()
    {
        Submission submission = UnderReview();
        submission.Decide(SubmissionOutcome.Approved, Captured.AddMinutes(3), 7uL);

        return submission;
    }

    private static Submission At(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Draft => New(),
        SubmissionStatus.Queued => Queued(),
        SubmissionStatus.UnderReview => UnderReview(),
        SubmissionStatus.Decided => Decided(),
        SubmissionStatus.Withdrawn => Withdrawn(),
        _ => New(),
    };

    private static Submission Withdrawn()
    {
        Submission submission = Queued();
        submission.Withdraw(Captured.AddMinutes(3), 7uL);

        return submission;
    }
}
