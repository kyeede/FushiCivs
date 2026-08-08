using Fushi.Core.Abstractions;
using Fushi.Core.Entities.Cycles;

namespace Fushi.Core.Entities.Guilds;

/// <summary>
/// A Discord server's configuration and the root its other records hang from.
/// </summary>
/// <remarks>
/// The primary key is the Discord guild snowflake rather than a generated
/// identifier. There is exactly one configuration per server and Discord has
/// already assigned it a permanent unique number, so inventing a second one
/// would only create the opportunity for the two to disagree.
/// <br/>
/// A row is created when the bot joins, before anybody has configured anything,
/// so every setting has a working default and <see cref="IsOperational"/> is
/// what decides whether the guild can actually run a cycle.
/// </remarks>
public sealed class Guild : AuditableEntity<ulong>
{
    private readonly List<VotingPermission> _votingPermissions = [];

    /// <summary>
    /// Initialises a guild with default configuration.
    /// </summary>
    /// <param name="id">The Discord guild snowflake.</param>
    /// <param name="createdAt">The instant the bot joined.</param>
    /// <param name="createdBy">
    /// The actor that added the bot, or <c>0</c> when unknown.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is <c>0</c>.
    /// </exception>
    public Guild(ulong id, DateTimeOffset createdAt, ulong createdBy)
        : base(id, createdAt, createdBy)
    {
        Channels = new GuildChannels();
        Policy = VotingPolicy.Default;
        Schedule = CycleSchedule.Default;
        IsEnabled = true;
    }

    private Guild()
    {
    }

    /// <summary>
    /// Gets the channels the bot is wired into.
    /// </summary>
    public GuildChannels Channels { get; private set; }

    /// <summary>
    /// Gets the rules that turn votes into decisions.
    /// </summary>
    public VotingPolicy Policy { get; private set; }

    /// <summary>
    /// Gets the recurring schedule cycles run on.
    /// </summary>
    public CycleSchedule Schedule { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the guild has switched the bot on.
    /// </summary>
    /// <remarks>
    /// Disabling stops new cycles from opening without discarding any
    /// configuration or history, which is what a server wants during a break.
    /// </remarks>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// Gets the grants describing who may vote here.
    /// </summary>
    public IReadOnlyCollection<VotingPermission> VotingPermissions => _votingPermissions;

    /// <summary>
    /// Gets a value indicating whether the guild is both switched on and
    /// configured well enough to run a cycle.
    /// </summary>
    public bool IsOperational => IsEnabled && !IsDeleted && Channels.IsReady;

    /// <summary>
    /// Replaces the channel routing wholesale.
    /// </summary>
    /// <remarks>
    /// Takes the complete set rather than one channel at a time so that a
    /// reconfiguration is a single atomic change. Partial updates should be
    /// built by the caller with a <c>with</c> expression on the current value.
    /// </remarks>
    /// <param name="channels">The new routing.</param>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor making the change.</param>
    public void ConfigureChannels(
        GuildChannels channels,
        DateTimeOffset updatedAt,
        ulong updatedBy)
    {
        Channels = channels;
        MarkUpdated(updatedAt, updatedBy);
    }

    /// <summary>
    /// Replaces the voting rules.
    /// </summary>
    /// <remarks>
    /// Takes effect on cycles opened afterwards. A cycle already in progress
    /// keeps the rules it opened under, so the bar cannot move while people are
    /// voting against it.
    /// </remarks>
    /// <param name="policy">The new rules.</param>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor making the change.</param>
    public void ConfigureVoting(VotingPolicy policy, DateTimeOffset updatedAt, ulong updatedBy)
    {
        Policy = policy;
        MarkUpdated(updatedAt, updatedBy);
    }

    /// <summary>
    /// Replaces the recurring schedule.
    /// </summary>
    /// <param name="schedule">The new schedule.</param>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor making the change.</param>
    /// <exception cref="ArgumentException">
    /// The schedule names a time zone this machine does not know.
    /// </exception>
    public void ConfigureSchedule(
        CycleSchedule schedule,
        DateTimeOffset updatedAt,
        ulong updatedBy)
    {
        if (!schedule.TryResolveTimeZone(out _))
        {
            throw new ArgumentException(
                $"'{schedule.TimeZoneId}' is not a time zone this system recognises.",
                nameof(schedule));
        }

        Schedule = schedule;
        MarkUpdated(updatedAt, updatedBy);
    }

    /// <summary>
    /// Switches the bot on or off for this guild.
    /// </summary>
    /// <param name="enabled">
    /// <see langword="true"/> to allow cycles to open.
    /// </param>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor making the change.</param>
    public void SetEnabled(bool enabled, DateTimeOffset updatedAt, ulong updatedBy)
    {
        if (IsEnabled == enabled)
        {
            return;
        }

        IsEnabled = enabled;
        MarkUpdated(updatedAt, updatedBy);
    }

    /// <summary>
    /// Adds a voting grant.
    /// </summary>
    /// <remarks>
    /// Adding a grant that already exists is a no-op, so a repeated command
    /// does not produce two rows that must both be revoked later.
    /// </remarks>
    /// <param name="permission">The grant to add.</param>
    /// <returns>
    /// <see langword="true"/> when the grant was added; <see langword="false"/>
    /// when an equivalent live grant was already present.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="permission"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="permission"/> belongs to a different guild.
    /// </exception>
    public bool Grant(VotingPermission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        if (permission.GuildId != Id)
        {
            throw new ArgumentException(
                "The grant belongs to a different guild.",
                nameof(permission));
        }

        bool exists = _votingPermissions.Exists(existing =>
            !existing.IsDeleted
            && existing.Scope == permission.Scope
            && existing.TargetId == permission.TargetId);

        if (exists)
        {
            return false;
        }

        _votingPermissions.Add(permission);
        return true;
    }

    /// <summary>
    /// Withdraws a voting grant.
    /// </summary>
    /// <remarks>
    /// The grant is soft-deleted rather than removed. Votes already cast under it
    /// have to remain explainable, and "who was allowed to vote at the time" is a
    /// question that gets asked precisely when somebody's rights have since been
    /// taken away.
    /// </remarks>
    /// <param name="scope">Whether a user or a role is being revoked.</param>
    /// <param name="targetId">The user or role snowflake.</param>
    /// <param name="revokedAt">The instant of the revocation.</param>
    /// <param name="revokedBy">The actor revoking it.</param>
    /// <returns>
    /// <see langword="true"/> when a live grant was withdrawn;
    /// <see langword="false"/> when there was none to withdraw.
    /// </returns>
    public bool Revoke(
        VotingPermissionScope scope,
        ulong targetId,
        DateTimeOffset revokedAt,
        ulong revokedBy)
    {
        VotingPermission? permission = _votingPermissions.Find(existing =>
            !existing.IsDeleted
            && existing.Scope == scope
            && existing.TargetId == targetId);

        if (permission is null)
        {
            return false;
        }

        permission.MarkDeleted(revokedAt, revokedBy);
        MarkUpdated(revokedAt, revokedBy);

        return true;
    }

    /// <summary>
    /// Finds a live voting grant.
    /// </summary>
    /// <param name="scope">Whether a user or a role is being looked for.</param>
    /// <param name="targetId">The user or role snowflake.</param>
    /// <returns>
    /// The grant, or <see langword="null"/> when none covers that target.
    /// </returns>
    public VotingPermission? FindGrant(VotingPermissionScope scope, ulong targetId)
        => _votingPermissions.Find(existing =>
            !existing.IsDeleted
            && existing.Scope == scope
            && existing.TargetId == targetId);

    /// <summary>
    /// Gets the grants that have not been withdrawn.
    /// </summary>
    /// <returns>The live grants.</returns>
    public IReadOnlyList<VotingPermission> LiveGrants()
        => _votingPermissions.FindAll(static permission => !permission.IsDeleted);

    /// <summary>
    /// Determines whether a caller may vote in this guild.
    /// </summary>
    /// <param name="userId">The user attempting to vote.</param>
    /// <param name="roleIds">The roles that user currently holds.</param>
    /// <returns>
    /// <see langword="true"/> when at least one live grant covers the caller.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="roleIds"/> is <see langword="null"/>.
    /// </exception>
    public bool CanVote(ulong userId, IReadOnlyCollection<ulong> roleIds)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        return _votingPermissions.Exists(permission => permission.Covers(userId, roleIds));
    }
}
