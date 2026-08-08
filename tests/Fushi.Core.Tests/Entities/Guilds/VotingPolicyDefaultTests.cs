using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;

namespace Fushi.Core.Tests.Entities.Guilds;

/// <summary>
/// Covers <see cref="VotingPolicy.Default"/>, which a guild inherits when it has
/// not configured its own rules.
/// </summary>
/// <remarks>
/// These exist because the default was written as <c>new()</c> and silently
/// returned a zero-initialised policy. A struct constructor whose parameters all
/// have defaults is not a parameterless constructor, so none of those defaults
/// applied. The result was no quorum, no abstaining, and no changing your mind —
/// none of which is what the type documents.
/// </remarks>
public sealed class VotingPolicyDefaultTests
{
    // The one that actually bit. Quorum has no fallback in its getter, because
    // zero is a legitimate setting meaning "no quorum gate", so nothing downstream
    // could have corrected this.
    [Fact]
    public void TheDefaultPolicyRequiresAQuorum()
    {
        VotingPolicy.Default.Quorum.ShouldBe(VotingPolicy.DEFAULT_QUORUM);
        VotingPolicy.Default.Quorum.ShouldNotBe(0);
    }

    [Fact]
    public void TheDefaultPolicyUsesTheDocumentedApprovalRatio()
    {
        VotingPolicy.Default.ApprovalRatio.ShouldBe(VotingPolicy.DEFAULT_APPROVAL_RATIO);
    }

    // Booleans cannot be repaired by a fallback in the getter: false is a real
    // setting, so "off" and "never set" are indistinguishable after the fact.
    // They have to be right at construction, which is the whole point here.
    [Fact]
    public void TheDefaultPolicyAllowsAbstainingAndChangingAVoteButNotSelfVoting()
    {
        VotingPolicy.Default.AllowAbstain.ShouldBeTrue();
        VotingPolicy.Default.AllowVoteChange.ShouldBeTrue();
        VotingPolicy.Default.AllowSelfVote.ShouldBeFalse();
    }

    // A single approval used to decide a submission outright, because a quorum of
    // zero passes any number of votes including one. This is that in behavioural
    // terms rather than as a property assertion.
    [Fact]
    public void ASingleVoteDoesNotDecideASubmissionUnderTheDefaultPolicy()
    {
        var lone = new VoteTally { Approvals = 1 };

        VotingPolicy.Default.Evaluate(lone).ShouldBe(SubmissionOutcome.Skipped);
    }
}
