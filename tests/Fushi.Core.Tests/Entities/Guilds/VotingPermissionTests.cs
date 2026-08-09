using Fushi.Core.Entities.Guilds;

namespace Fushi.Core.Tests.Entities.Guilds;

/// <summary>
/// Covers <see cref="VotingPermission"/>: which callers a grant covers, the
/// effect of revoking one, the sigil it renders with, and the values it refuses
/// to be built from.
/// </summary>
public sealed class VotingPermissionTests
{
    private static readonly DateTimeOffset Granted = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AUserGrantCoversOnlyItsOwnTarget()
    {
        VotingPermission permission = Permission(VotingPermissionScope.User, 9uL);

        permission.Covers(9uL, NoRoles).ShouldBeTrue();
        permission.Covers(10uL, NoRoles).ShouldBeFalse();
        permission.Covers(10uL, Roles(9uL)).ShouldBeFalse();
    }

    // Roles are resolved at the moment of the attempt rather than stored, so
    // removing somebody from a role takes effect straight away.
    [Fact]
    public void ARoleGrantCoversWhoeverCurrentlyHoldsTheRole()
    {
        VotingPermission permission = Permission(VotingPermissionScope.Role, 500uL);

        permission.Covers(9uL, Roles(500uL)).ShouldBeTrue();
        permission.Covers(9uL, Roles(499uL, 500uL)).ShouldBeTrue();
        permission.Covers(9uL, Roles(499uL)).ShouldBeFalse();
        permission.Covers(500uL, NoRoles).ShouldBeFalse();
    }

    [Fact]
    public void ARevokedGrantCoversNobody()
    {
        VotingPermission permission = Permission(VotingPermissionScope.User, 9uL);

        permission.MarkDeleted(Granted.AddHours(1), deletedBy: 7uL);

        permission.Covers(9uL, NoRoles).ShouldBeFalse();
    }

    [Fact]
    public void AGrantRendersWithTheSigilMatchingItsScope()
    {
        Permission(VotingPermissionScope.User, 9uL).Mention.ShouldBe("<@9>");
        Permission(VotingPermissionScope.Role, 500uL).Mention.ShouldBe("<@&500>");
    }

    [Fact]
    public void ANoteIsTrimmedAndAnEmptyOneIsRecordedAsAbsent()
    {
        var permission = VotingPermission.Create(
            1uL,
            VotingPermissionScope.User,
            9uL,
            Granted,
            createdBy: 7uL,
            note: "  Trusted reviewer.  ");

        permission.Note.ShouldBe("Trusted reviewer.");

        permission.SetNote("   ", Granted.AddHours(1), 7uL);

        permission.Note.ShouldBeNull();
        permission.UpdatedAt.ShouldBe(Granted.AddHours(1));
    }

    [Fact]
    public void AGrantWithoutANoteRecordsNone() => Permission(VotingPermissionScope.User, 9uL).Note.ShouldBeNull();

    [Theory]
    [InlineData(0uL, 9uL)]
    [InlineData(1uL, 0uL)]
    public void AGrantNeedsARealGuildAndARealTarget(ulong guildId, ulong targetId)
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(
            () => VotingPermission.Create(guildId, VotingPermissionScope.User, targetId, Granted, 7uL));
    }

    [Fact]
    public void AnUndefinedScopeIsRefused()
    {
        _ = Should.Throw<ArgumentException>(
            () => VotingPermission.Create(1uL, (VotingPermissionScope)99, 9uL, Granted, 7uL));
    }

    private static ulong[] NoRoles => [];

    private static ulong[] Roles(params ulong[] roleIds) => roleIds;

    private static VotingPermission Permission(VotingPermissionScope scope, ulong targetId)
        => VotingPermission.Create(1uL, scope, targetId, Granted, createdBy: 7uL);
}
