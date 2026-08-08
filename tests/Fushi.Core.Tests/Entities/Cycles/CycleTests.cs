using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;

namespace Fushi.Core.Tests.Entities.Cycles;

/// <summary>
/// Covers <see cref="Cycle"/>: the lifecycle moves it permits and refuses, the
/// two conditions a vote has to satisfy to be accepted, and the terms it copies
/// so a result stays explainable later.
/// </summary>
public sealed class CycleTests
{
    private static readonly DateTimeOffset Opens = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Closes = new(2026, 8, 10, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewCycleStartsScheduledAndCopiesItsWindowAndTerms()
    {
        VotingPolicy policy = new(approvalRatio: 0.75d, quorum: 5);

        Cycle cycle = Cycle.Create(guildId: 1uL, Window(), policy, Opens.AddDays(-1), createdBy: 7uL);

        cycle.Status.ShouldBe(CycleStatus.Scheduled);
        cycle.OpensAt.ShouldBe(Opens);
        cycle.ClosesAt.ShouldBe(Closes);
        cycle.Window.ShouldBe(Window());
        cycle.Policy.ShouldBe(policy);
        cycle.Code.IsEmpty.ShouldBeFalse();
        cycle.IsTerminal.ShouldBeFalse();
    }

    [Theory]
    [InlineData(CycleStatus.Scheduled, CycleStatus.Open)]
    [InlineData(CycleStatus.Open, CycleStatus.Closed)]
    [InlineData(CycleStatus.Closed, CycleStatus.Finalised)]
    [InlineData(CycleStatus.Scheduled, CycleStatus.Cancelled)]
    [InlineData(CycleStatus.Open, CycleStatus.Cancelled)]
    [InlineData(CycleStatus.Closed, CycleStatus.Cancelled)]
    public void TheLifecycleMovesForwardsAndCanAlwaysBeAbandoned(CycleStatus from, CycleStatus to)
    {
        Cycle cycle = Advanced(from);

        cycle.TransitionTo(to, Opens, updatedBy: 7uL);

        cycle.Status.ShouldBe(to);
    }

    // The scheduler is retried on failure, so asking for the state a cycle is
    // already in has to be harmless rather than an error.
    [Fact]
    public void RepeatingTheCurrentStateIsANoOpRatherThanAnError()
    {
        Cycle cycle = Advanced(CycleStatus.Open);

        cycle.TransitionTo(CycleStatus.Open, Opens, updatedBy: 7uL);

        cycle.Status.ShouldBe(CycleStatus.Open);
        cycle.UpdatedBy.ShouldNotBe(7uL);
    }

    [Theory]
    [InlineData(CycleStatus.Finalised, CycleStatus.Open)]
    [InlineData(CycleStatus.Finalised, CycleStatus.Cancelled)]
    [InlineData(CycleStatus.Cancelled, CycleStatus.Open)]
    [InlineData(CycleStatus.Closed, CycleStatus.Open)]
    [InlineData(CycleStatus.Scheduled, CycleStatus.Closed)]
    [InlineData(CycleStatus.Scheduled, CycleStatus.Finalised)]
    [InlineData(CycleStatus.Open, CycleStatus.Finalised)]
    [InlineData(CycleStatus.Open, CycleStatus.Scheduled)]
    public void AnImpossibleLifecycleMoveIsRefused(CycleStatus from, CycleStatus to)
    {
        Cycle cycle = Advanced(from);

        _ = Should.Throw<InvalidOperationException>(() => cycle.TransitionTo(to, Opens, 7uL));

        cycle.Status.ShouldBe(from);
    }

    [Fact]
    public void OnlyFinalisedAndCancelledAreTerminal()
    {
        Advanced(CycleStatus.Scheduled).IsTerminal.ShouldBeFalse();
        Advanced(CycleStatus.Open).IsTerminal.ShouldBeFalse();
        Advanced(CycleStatus.Closed).IsTerminal.ShouldBeFalse();
        Advanced(CycleStatus.Finalised).IsTerminal.ShouldBeTrue();
        Advanced(CycleStatus.Cancelled).IsTerminal.ShouldBeTrue();
    }

    // The status lags the clock by however long the scheduler takes to notice, so
    // a vote arriving in that gap is late even though the row still says Open.
    [Fact]
    public void AVoteIsAcceptedOnlyWhenTheStatusAndTheClockBothAgree()
    {
        Cycle cycle = Advanced(CycleStatus.Open);

        cycle.IsAcceptingVotes(Opens.AddSeconds(-1)).ShouldBeFalse();
        cycle.IsAcceptingVotes(Opens).ShouldBeTrue();
        cycle.IsAcceptingVotes(Closes.AddSeconds(-1)).ShouldBeTrue();
        cycle.IsAcceptingVotes(Closes).ShouldBeFalse();
    }

    [Fact]
    public void AScheduledCycleInsideItsOwnWindowStillRefusesVotes()
    {
        Cycle cycle = Advanced(CycleStatus.Scheduled);

        cycle.Window.Contains(Opens).ShouldBeTrue();
        cycle.IsAcceptingVotes(Opens).ShouldBeFalse();
    }

    [Fact]
    public void ADeletedCycleRefusesVotesEvenWhileItIsOpen()
    {
        Cycle cycle = Advanced(CycleStatus.Open);

        cycle.MarkDeleted(Opens, deletedBy: 7uL);

        cycle.IsAcceptingVotes(Opens.AddHours(1)).ShouldBeFalse();
    }

    [Fact]
    public void RecordingTheAnnouncementAndResultsMessagesStampsTheChange()
    {
        Cycle cycle = Advanced(CycleStatus.Open);

        cycle.SetAnnouncementMessage(11uL, Opens, updatedBy: 7uL);
        cycle.SetResultsMessage(22uL, Closes, updatedBy: 7uL);

        cycle.AnnouncementMessageId.ShouldBe(11uL);
        cycle.ResultsMessageId.ShouldBe(22uL);
        cycle.UpdatedAt.ShouldBe(Closes);
    }

    [Fact]
    public void AMessageSnowflakeCannotBeZero()
    {
        Cycle cycle = Advanced(CycleStatus.Open);

        _ = Should.Throw<ArgumentOutOfRangeException>(() => cycle.SetAnnouncementMessage(0uL, Opens, 7uL));
        _ = Should.Throw<ArgumentOutOfRangeException>(() => cycle.SetResultsMessage(0uL, Opens, 7uL));
    }

    [Fact]
    public void AttachingTheSameSubmissionTwiceAddsItOnce()
    {
        Cycle cycle = New();
        Submission submission = NewSubmission(guildId: 1uL);

        cycle.Attach(submission);
        cycle.Attach(submission);

        cycle.Submissions.Count.ShouldBe(1);
    }

    [Fact]
    public void ASubmissionFromAnotherGuildCannotBeAttached()
    {
        Cycle cycle = New();

        _ = Should.Throw<ArgumentException>(() => cycle.Attach(NewSubmission(guildId: 2uL)));
    }

    [Fact]
    public void AFinishedCycleTakesNoFurtherSubmissions()
    {
        Cycle cycle = Advanced(CycleStatus.Cancelled);

        _ = Should.Throw<InvalidOperationException>(() => cycle.Attach(NewSubmission(guildId: 1uL)));
    }

    [Fact]
    public void ACycleNeedsAGuildAndACode()
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(
            () => Cycle.Create(0uL, Window(), VotingPolicy.Default, Opens, 7uL));

        _ = Should.Throw<ArgumentException>(() => new Cycle(
            Guid.NewGuid(),
            ShortCode.Empty,
            1uL,
            Window(),
            VotingPolicy.Default,
            Opens,
            7uL));
    }

    private static CycleWindow Window() => new(new DateOnly(2026, 8, 10), Opens, Closes);

    private static Cycle New()
        => Cycle.Create(guildId: 1uL, Window(), VotingPolicy.Default, Opens.AddDays(-1), createdBy: 7uL);

    private static Cycle Advanced(CycleStatus status)
    {
        Cycle cycle = New();
        DateTimeOffset at = Opens.AddDays(-1);

        foreach (CycleStatus step in Route(status))
        {
            cycle.TransitionTo(step, at, updatedBy: 1uL);
        }

        return cycle;
    }

    private static IEnumerable<CycleStatus> Route(CycleStatus status) => status switch
    {
        CycleStatus.Scheduled => [],
        CycleStatus.Open => [CycleStatus.Open],
        CycleStatus.Closed => [CycleStatus.Open, CycleStatus.Closed],
        CycleStatus.Finalised => [CycleStatus.Open, CycleStatus.Closed, CycleStatus.Finalised],
        CycleStatus.Cancelled => [CycleStatus.Cancelled],
        _ => [],
    };

    private static Submission NewSubmission(ulong guildId)
        => Submission.Create(guildId, 5uL, 6uL, 7uL, "A title", "Some content.", Opens.AddDays(-2));
}
