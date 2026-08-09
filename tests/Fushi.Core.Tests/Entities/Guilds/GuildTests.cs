using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;

namespace Fushi.Core.Tests.Entities.Guilds;

/// <summary>
/// Covers <see cref="Guild"/>: the working configuration a guild starts with,
/// what it takes to become operational, and the additive, deny-by-default
/// permission model behind <see cref="Guild.CanVote"/>.
/// </summary>
public sealed class GuildTests
{
    private static readonly DateTimeOffset Joined = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    // A row exists from the moment the bot joins, before anybody configures
    // anything, so every setting has to be usable on its own.
    [Fact]
    public void ANewGuildStartsEnabledWithTheDocumentedDefaults()
    {
        Guild guild = New();

        guild.IsEnabled.ShouldBeTrue();
        guild.Policy.ShouldBe(VotingPolicy.Default);
        guild.Schedule.ShouldBe(CycleSchedule.Default);
        guild.Schedule.Days.ShouldBe(CycleDays.Standard);
        guild.VotingPermissions.ShouldBeEmpty();
    }

    // Being switched on is not the same as being wired up: without an intake and
    // a review channel there is nowhere to collect from or post to.
    [Fact]
    public void AGuildIsOperationalOnlyOnceItIsBothEnabledAndWiredUp()
    {
        Guild guild = New();

        guild.IsOperational.ShouldBeFalse();

        guild.ConfigureChannels(Wired(), Joined.AddHours(1), 7uL);
        guild.IsOperational.ShouldBeTrue();

        guild.SetEnabled(enabled: false, Joined.AddHours(2), 7uL);
        guild.IsOperational.ShouldBeFalse();
    }

    [Fact]
    public void ADeletedGuildIsNotOperationalHoweverWellConfigured()
    {
        Guild guild = New();
        guild.ConfigureChannels(Wired(), Joined.AddHours(1), 7uL);

        guild.MarkDeleted(Joined.AddHours(2), deletedBy: 7uL);

        guild.IsOperational.ShouldBeFalse();
    }

    [Fact]
    public void SwitchingToTheStateItIsAlreadyInChangesNothing()
    {
        Guild guild = New();

        guild.SetEnabled(enabled: true, Joined.AddHours(1), 7uL);

        guild.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public void ReconfiguringVotingReplacesThePolicyAndStampsTheChange()
    {
        Guild guild = New();
        VotingPolicy policy = new(approvalRatio: 0.75d, quorum: 5);

        guild.ConfigureVoting(policy, Joined.AddHours(1), 7uL);

        guild.Policy.ShouldBe(policy);
        guild.UpdatedAt.ShouldBe(Joined.AddHours(1));
        guild.UpdatedBy.ShouldBe(7uL);
    }

    [Fact]
    public void AScheduleNamingAnUnknownTimeZoneIsRefused()
    {
        Guild guild = New();
        CycleSchedule broken = new(CycleDays.Standard, timeZoneId: "Mars/Olympus_Mons");

        _ = Should.Throw<ArgumentException>(
            () => guild.ConfigureSchedule(broken, Joined.AddHours(1), 7uL));

        guild.Schedule.ShouldBe(CycleSchedule.Default);
    }

    [Fact]
    public void GrantingTheSameRightTwiceAddsOneGrant()
    {
        Guild guild = New();

        guild.Grant(Permission(VotingPermissionScope.User, 9uL)).ShouldBeTrue();
        guild.Grant(Permission(VotingPermissionScope.User, 9uL)).ShouldBeFalse();

        guild.LiveGrants().Count.ShouldBe(1);
    }

    // The scope is part of the identity of a grant: a role and a user that happen
    // to share a snowflake are different subjects.
    [Fact]
    public void AUserGrantAndARoleGrantWithTheSameTargetAreDifferentGrants()
    {
        Guild guild = New();

        guild.Grant(Permission(VotingPermissionScope.User, 9uL)).ShouldBeTrue();
        guild.Grant(Permission(VotingPermissionScope.Role, 9uL)).ShouldBeTrue();

        guild.LiveGrants().Count.ShouldBe(2);
    }

    [Fact]
    public void AGrantBelongingToAnotherGuildIsRefused()
    {
        Guild guild = New();

        _ = Should.Throw<ArgumentException>(
            () => guild.Grant(VotingPermission.Create(2uL, VotingPermissionScope.User, 9uL, Joined, 7uL)));
    }

    // Revoking soft-deletes rather than removes, because "who was allowed to vote
    // at the time" gets asked precisely when somebody's rights have been taken
    // away.
    [Fact]
    public void RevokingHidesTheGrantWithoutDiscardingIt()
    {
        Guild guild = New();
        _ = guild.Grant(Permission(VotingPermissionScope.User, 9uL));

        guild.Revoke(VotingPermissionScope.User, 9uL, Joined.AddHours(1), 7uL).ShouldBeTrue();

        guild.LiveGrants().ShouldBeEmpty();
        guild.FindGrant(VotingPermissionScope.User, 9uL).ShouldBeNull();
        guild.VotingPermissions.Count.ShouldBe(1);
        guild.UpdatedAt.ShouldBe(Joined.AddHours(1));
    }

    [Fact]
    public void RevokingSomethingNobodyWasGrantedReportsThatNothingHappened()
    {
        Guild guild = New();

        guild.Revoke(VotingPermissionScope.User, 9uL, Joined.AddHours(1), 7uL).ShouldBeFalse();
        guild.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public void RevokingTwiceReportsThatTheSecondAttemptFoundNothing()
    {
        Guild guild = New();
        _ = guild.Grant(Permission(VotingPermissionScope.User, 9uL));
        _ = guild.Revoke(VotingPermissionScope.User, 9uL, Joined.AddHours(1), 7uL);

        guild.Revoke(VotingPermissionScope.User, 9uL, Joined.AddHours(2), 7uL).ShouldBeFalse();
    }

    // A revoked grant leaves nothing behind that blocks a later regrant.
    [Fact]
    public void ARightCanBeGrantedAgainAfterBeingRevoked()
    {
        Guild guild = New();
        _ = guild.Grant(Permission(VotingPermissionScope.User, 9uL));
        _ = guild.Revoke(VotingPermissionScope.User, 9uL, Joined.AddHours(1), 7uL);

        guild.Grant(Permission(VotingPermissionScope.User, 9uL)).ShouldBeTrue();

        guild.LiveGrants().Count.ShouldBe(1);
    }

    // Deny by default: a misconfiguration locks people out, which someone reports,
    // rather than letting the wrong people decide applications, which nobody
    // notices.
    [Fact]
    public void NobodyMayVoteUntilSomebodyIsGranted() => New().CanVote(9uL, NoRoles).ShouldBeFalse();

    [Fact]
    public void AUserGrantCoversThatUserAndNobodyElse()
    {
        Guild guild = New();
        _ = guild.Grant(Permission(VotingPermissionScope.User, 9uL));

        guild.CanVote(9uL, NoRoles).ShouldBeTrue();
        guild.CanVote(10uL, NoRoles).ShouldBeFalse();
    }

    [Fact]
    public void ARoleGrantCoversAnybodyHoldingThatRole()
    {
        Guild guild = New();
        _ = guild.Grant(Permission(VotingPermissionScope.Role, 500uL));

        guild.CanVote(9uL, Roles(500uL)).ShouldBeTrue();
        guild.CanVote(10uL, Roles(500uL, 501uL)).ShouldBeTrue();
        guild.CanVote(9uL, Roles(501uL)).ShouldBeFalse();
        guild.CanVote(9uL, NoRoles).ShouldBeFalse();
    }

    // Grants are additive with no deny rule, so losing one route in still leaves
    // the other.
    [Fact]
    public void LosingARoleStillLeavesADirectUserGrantIntact()
    {
        Guild guild = New();
        _ = guild.Grant(Permission(VotingPermissionScope.User, 9uL));
        _ = guild.Grant(Permission(VotingPermissionScope.Role, 500uL));

        guild.CanVote(9uL, NoRoles).ShouldBeTrue();

        _ = guild.Revoke(VotingPermissionScope.User, 9uL, Joined.AddHours(1), 7uL);

        guild.CanVote(9uL, NoRoles).ShouldBeFalse();
        guild.CanVote(9uL, Roles(500uL)).ShouldBeTrue();
    }

    [Fact]
    public void ARevokedGrantStopsCoveringItsTargetImmediately()
    {
        Guild guild = New();
        _ = guild.Grant(Permission(VotingPermissionScope.User, 9uL));
        _ = guild.Revoke(VotingPermissionScope.User, 9uL, Joined.AddHours(1), 7uL);

        guild.CanVote(9uL, NoRoles).ShouldBeFalse();
    }

    [Fact]
    public void CheckingWhoMayVoteNeedsTheCallersRoles()
    {
        _ = Should.Throw<ArgumentNullException>(() => New().CanVote(9uL, null!));
        _ = Should.Throw<ArgumentNullException>(() => New().Grant(null!));
    }

    [Fact]
    public void AGuildIsIdentifiedByItsDiscordSnowflake()
    {
        _ = Should.Throw<ArgumentException>(() => new Guild(0uL, Joined, 7uL));

        New().Id.ShouldBe(1uL);
    }

    private static ulong[] NoRoles => [];

    private static ulong[] Roles(params ulong[] roleIds) => roleIds;

    private static Guild New() => new(1uL, Joined, createdBy: 7uL);

    private static GuildChannels Wired()
        => new(intakeChannelId: 100uL, reviewChannelId: 200uL);

    private static VotingPermission Permission(VotingPermissionScope scope, ulong targetId)
        => VotingPermission.Create(1uL, scope, targetId, Joined, createdBy: 7uL);
}
