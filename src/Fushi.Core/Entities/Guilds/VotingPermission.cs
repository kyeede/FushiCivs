using Fushi.Core.Abstractions;
using Fushi.Core.Utilities;

namespace Fushi.Core.Entities.Guilds;

/// <summary>
/// A grant allowing a user, or everyone holding a role, to vote in a guild.
/// </summary>
/// <remarks>
/// Voting is denied by default and opened by grant, rather than allowed by
/// default and closed by exception. A configuration mistake therefore locks
/// people out, which someone notices and reports, instead of quietly letting
/// the wrong people decide applications, which nobody notices at all.
/// <br/>
/// Grants are additive: holding any matching grant is enough. There is no deny
/// rule, because a deny rule that loses to an allow rule is a permission system
/// nobody can reason about. Revoking is done by removing the grant.
/// </remarks>
/// <seealso cref="VotingPermissionScope"/>
public sealed class VotingPermission : AuditableEntity<Guid>
{
    /// <summary>
    /// Initialises a grant.
    /// </summary>
    /// <param name="id">The permanent identifier.</param>
    /// <param name="guildId">The guild the grant applies in.</param>
    /// <param name="scope">Whether the grant targets a user or a role.</param>
    /// <param name="targetId">
    /// The snowflake of the granted user or role.
    /// </param>
    /// <param name="createdAt">The instant the grant was made.</param>
    /// <param name="createdBy">The actor that made the grant.</param>
    /// <param name="note">
    /// An optional reason, shown when the grant list is displayed.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is empty, or <paramref name="scope"/> is not a
    /// defined value.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="guildId"/> or <paramref name="targetId"/> is <c>0</c>.
    /// </exception>
    public VotingPermission(
        Guid id,
        ulong guildId,
        VotingPermissionScope scope,
        ulong targetId,
        DateTimeOffset createdAt,
        ulong createdBy,
        string? note = null)
        : base(id, createdAt, createdBy)
    {
        ArgumentOutOfRangeException.ThrowIfZero(guildId);
        ArgumentOutOfRangeException.ThrowIfZero(targetId);

        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentException($"'{scope}' is not a defined scope.", nameof(scope));
        }

        GuildId = guildId;
        Scope = scope;
        TargetId = targetId;
        Note = Trim(note);
    }

    private VotingPermission()
    {
    }

    /// <summary>
    /// Gets the guild this grant applies in.
    /// </summary>
    public ulong GuildId { get; private set; }

    /// <summary>
    /// Gets whether the grant targets a user or a role.
    /// </summary>
    public VotingPermissionScope Scope { get; private set; }

    /// <summary>
    /// Gets the snowflake of the granted user or role.
    /// </summary>
    public ulong TargetId { get; private set; }

    /// <summary>
    /// Gets the reason recorded when the grant was made.
    /// </summary>
    /// <value>
    /// The note, or <see langword="null"/> when none was given.
    /// </value>
    public string? Note { get; private set; }

    /// <summary>
    /// Gets a Discord mention for the grant target.
    /// </summary>
    /// <remarks>
    /// Renders with the sigil matching <see cref="Scope"/>, so a grant list can
    /// be printed without the caller having to branch on the scope itself.
    /// </remarks>
    public string Mention => Scope switch
    {
        VotingPermissionScope.User => MentionUtility.User(TargetId),
        VotingPermissionScope.Role => MentionUtility.Role(TargetId),
        _ => MentionUtility.User(TargetId),
    };

    /// <summary>
    /// Creates a grant with a freshly generated identifier.
    /// </summary>
    /// <param name="guildId">The guild the grant applies in.</param>
    /// <param name="scope">Whether the grant targets a user or a role.</param>
    /// <param name="targetId">The snowflake of the granted user or role.</param>
    /// <param name="createdAt">The instant the grant was made.</param>
    /// <param name="createdBy">The actor that made the grant.</param>
    /// <param name="note">An optional reason.</param>
    /// <returns>The new grant.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="guildId"/> or <paramref name="targetId"/> is <c>0</c>.
    /// </exception>
    public static VotingPermission Create(
        ulong guildId,
        VotingPermissionScope scope,
        ulong targetId,
        DateTimeOffset createdAt,
        ulong createdBy,
        string? note = null)
        => new(Guid.CreateVersion7(createdAt), guildId, scope, targetId, createdAt, createdBy, note);

    /// <summary>
    /// Determines whether this grant covers a specific caller.
    /// </summary>
    /// <param name="userId">The user attempting to vote.</param>
    /// <param name="roleIds">
    /// The roles that user currently holds, resolved at the time of the
    /// attempt rather than stored, so that removing a role takes effect
    /// immediately.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the grant permits this caller to vote.
    /// </returns>
    public bool Covers(ulong userId, IReadOnlyCollection<ulong> roleIds)
    {
        if (IsDeleted)
        {
            return false;
        }

        return Scope switch
        {
            VotingPermissionScope.User => TargetId == userId,
            VotingPermissionScope.Role => roleIds is not null && roleIds.Contains(TargetId),
            _ => false,
        };
    }

    /// <summary>
    /// Replaces the recorded reason for the grant.
    /// </summary>
    /// <param name="note">The new reason, or <see langword="null"/> to clear it.</param>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor making the change.</param>
    public void SetNote(string? note, DateTimeOffset updatedAt, ulong updatedBy)
    {
        Note = Trim(note);
        MarkUpdated(updatedAt, updatedBy);
    }

    private static string? Trim(string? note)
        => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
