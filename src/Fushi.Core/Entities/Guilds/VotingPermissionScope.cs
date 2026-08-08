namespace Fushi.Core.Entities.Guilds;

/// <summary>
/// What kind of Discord principal a voting grant points at.
/// </summary>
/// <seealso cref="VotingPermission"/>
public enum VotingPermissionScope
{
    /// <summary>
    /// A single user, identified by their user snowflake. Survives the user
    /// losing every role, which is what makes it the right tool for a
    /// one-person exception.
    /// </summary>
    User = 0,

    /// <summary>
    /// Everyone holding a role, identified by the role snowflake. Membership is
    /// resolved at the moment a vote is cast, so granting the role is enough
    /// and no further configuration is needed per person.
    /// </summary>
    Role = 1,
}
