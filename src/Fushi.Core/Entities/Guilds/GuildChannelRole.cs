namespace Fushi.Core.Entities.Guilds;

/// <summary>
/// One of the jobs a guild can assign a channel to.
/// </summary>
/// <remarks>
/// Named rather than left as five separate settings so that "change the intake
/// channel" is one value travelling through the system instead of a position in
/// an argument list. That matters most at the edges: a component identifier can
/// carry a role, and a panel can be built for a role, without either of them
/// knowing which field of <see cref="GuildChannels"/> it corresponds to.
/// </remarks>
public enum GuildChannelRole
{
    /// <summary>Where applications are collected from.</summary>
    Intake = 0,

    /// <summary>Where applications are posted for voting.</summary>
    Review = 1,

    /// <summary>Where a cycle's outcome is announced.</summary>
    Results = 2,

    /// <summary>Where decided applications are kept.</summary>
    Archive = 3,

    /// <summary>Where the audit trail is echoed.</summary>
    Log = 4,
}
