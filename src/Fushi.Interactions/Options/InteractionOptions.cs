namespace Fushi.Interactions.Options;

/// <summary>
/// The settings that decide where slash commands are registered.
/// </summary>
/// <remarks>
/// Bound to the same <c>Discord</c> configuration section the gateway reads,
/// because <c>Discord__DevelopmentGuildId</c> is one setting and documenting it
/// twice under two names would be worse than binding it twice. This project
/// deliberately does not reference <c>Fushi.Gateway</c>, so it cannot reuse that
/// project's options class and declares the one property it needs instead.
/// </remarks>
public sealed class InteractionOptions
{
    /// <summary>
    /// The configuration section these settings are bound to.
    /// </summary>
    public const string SECTION = "Discord";

    /// <summary>
    /// Gets or sets the guild that commands are registered to during development.
    /// </summary>
    /// <value>
    /// A guild snowflake, or <see langword="null"/> to register globally.
    /// </value>
    /// <remarks>
    /// Guild commands appear the moment they are registered, while global ones
    /// propagate across Discord over the following hour. During development that
    /// is the difference between iterating in seconds and iterating in an hour,
    /// which is the whole reason this setting exists. Leave it unset in
    /// production, where the bot is in more than one guild.
    /// </remarks>
    public ulong? DevelopmentGuildId { get; set; }
}
