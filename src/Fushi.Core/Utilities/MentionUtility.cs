using System.Globalization;

namespace Fushi.Core.Utilities;

/// <summary>
/// Builds and reads the markup Discord uses to reference users, roles,
/// channels, and instants inside message content.
/// </summary>
/// <remarks>
/// The sigil carries the meaning: <c>&lt;@id&gt;</c> is a user,
/// <c>&lt;@&amp;id&gt;</c> is a role, and <c>&lt;#id&gt;</c> is a channel.
/// Getting one wrong produces a message that renders as literal text rather
/// than a link, which is the kind of defect that survives review because it
/// looks fine in source. Centralising the construction removes the opportunity.
/// <br/>
/// Parsing is tolerant of the legacy nickname form <c>&lt;@!id&gt;</c>, which
/// Discord no longer emits but which still exists in older stored content.
/// </remarks>
/// <seealso href="https://discord.com/developers/docs/reference#message-formatting">
/// Discord developer documentation: Message formatting
/// </seealso>
public static class MentionUtility
{
    /// <summary>
    /// Builds a user mention.
    /// </summary>
    /// <param name="userId">The user's snowflake.</param>
    /// <returns>Markup of the form <c>&lt;@123&gt;</c>.</returns>
    public static string User(ulong userId) => string.Create(CultureInfo.InvariantCulture, $"<@{userId}>");

    /// <summary>
    /// Builds a role mention.
    /// </summary>
    /// <remarks>
    /// Whether the mention actually pings anybody depends on the allowed
    /// mentions of the outgoing message, not on this markup. Announcement
    /// messages should suppress role pings explicitly rather than relying on
    /// the role's own settings.
    /// </remarks>
    /// <param name="roleId">The role's snowflake.</param>
    /// <returns>Markup of the form <c>&lt;@&amp;123&gt;</c>.</returns>
    public static string Role(ulong roleId) => string.Create(CultureInfo.InvariantCulture, $"<@&{roleId}>");

    /// <summary>
    /// Builds a channel link.
    /// </summary>
    /// <param name="channelId">The channel's snowflake.</param>
    /// <returns>Markup of the form <c>&lt;#123&gt;</c>.</returns>
    public static string Channel(ulong channelId) => string.Create(CultureInfo.InvariantCulture, $"<#{channelId}>");

    /// <summary>
    /// Builds a slash command link that a user can click to invoke the command.
    /// </summary>
    /// <param name="name">
    /// The fully qualified command name, such as <c>submission view</c> for a
    /// subcommand.
    /// </param>
    /// <param name="commandId">
    /// The registered command's snowflake, as returned by Discord when the
    /// command was created.
    /// </param>
    /// <returns>Markup of the form <c>&lt;/submission view:123&gt;</c>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty or consists only of white space.
    /// </exception>
    public static string Command(string name, ulong commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return string.Create(CultureInfo.InvariantCulture, $"</{name}:{commandId}>");
    }

    /// <summary>
    /// Builds a timestamp that each client renders in its own locale and time
    /// zone.
    /// </summary>
    /// <param name="instant">The instant to render.</param>
    /// <param name="style">How the client should format it.</param>
    /// <returns>Markup of the form <c>&lt;t:1745160000:R&gt;</c>.</returns>
    public static string Timestamp(DateTimeOffset instant, TimestampStyle style = TimestampStyle.ShortDateTime)
    {
        char suffix = style switch
        {
            TimestampStyle.ShortTime => 't',
            TimestampStyle.LongTime => 'T',
            TimestampStyle.ShortDate => 'd',
            TimestampStyle.LongDate => 'D',
            TimestampStyle.ShortDateTime => 'f',
            TimestampStyle.LongDateTime => 'F',
            TimestampStyle.Relative => 'R',
            _ => 'f',
        };

        long seconds = instant.ToUnixTimeSeconds();
        return string.Create(CultureInfo.InvariantCulture, $"<t:{seconds}:{suffix}>");
    }

    /// <summary>
    /// Attempts to read the snowflake out of a user mention.
    /// </summary>
    /// <param name="value">
    /// The markup to read, in either the <c>&lt;@id&gt;</c> or the legacy
    /// <c>&lt;@!id&gt;</c> form.
    /// </param>
    /// <param name="userId">
    /// When this method returns <see langword="true"/>, the mentioned user's
    /// snowflake; otherwise <c>0</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value was a user mention; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryParseUser(ReadOnlySpan<char> value, out ulong userId)
    {
        ReadOnlySpan<char> body = Unwrap(value, '@');
        if (!body.IsEmpty && body[0] == '!')
        {
            body = body[1..];
        }

        return TryParseSnowflake(body, out userId);
    }

    /// <summary>
    /// Attempts to read the snowflake out of a role mention.
    /// </summary>
    /// <param name="value">The markup to read, in the <c>&lt;@&amp;id&gt;</c> form.</param>
    /// <param name="roleId">
    /// When this method returns <see langword="true"/>, the mentioned role's
    /// snowflake; otherwise <c>0</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value was a role mention; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryParseRole(ReadOnlySpan<char> value, out ulong roleId)
    {
        ReadOnlySpan<char> body = Unwrap(value, '@');
        if (body.IsEmpty || body[0] != '&')
        {
            roleId = 0uL;
            return false;
        }

        return TryParseSnowflake(body[1..], out roleId);
    }

    /// <summary>
    /// Attempts to read the snowflake out of a channel link.
    /// </summary>
    /// <param name="value">The markup to read, in the <c>&lt;#id&gt;</c> form.</param>
    /// <param name="channelId">
    /// When this method returns <see langword="true"/>, the linked channel's
    /// snowflake; otherwise <c>0</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value was a channel link; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryParseChannel(ReadOnlySpan<char> value, out ulong channelId) =>
        TryParseSnowflake(Unwrap(value, '#'), out channelId);

    /// <summary>
    /// Attempts to read a snowflake from either a mention or a bare identifier.
    /// </summary>
    /// <remarks>
    /// Users supply identifiers both ways — pasted from Developer Mode as a
    /// bare number, or typed as a mention that the client expands. Accepting
    /// both is the difference between a command that works and one that needs
    /// explaining.
    /// </remarks>
    /// <param name="value">The text to read.</param>
    /// <param name="snowflake">
    /// When this method returns <see langword="true"/>, the parsed snowflake;
    /// otherwise <c>0</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a snowflake was found; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryParseAny(ReadOnlySpan<char> value, out ulong snowflake)
    {
        ReadOnlySpan<char> trimmed = value.Trim();

        if (
            TryParseUser(trimmed, out snowflake)
            || TryParseRole(trimmed, out snowflake)
            || TryParseChannel(trimmed, out snowflake)
        )
        {
            return true;
        }

        return TryParseSnowflake(trimmed, out snowflake);
    }

    private static ReadOnlySpan<char> Unwrap(ReadOnlySpan<char> value, char sigil)
    {
        ReadOnlySpan<char> trimmed = value.Trim();

        // Shortest possible mention is "<@1>": angle brackets, sigil, one digit.
        if (trimmed.Length < 4 || trimmed[0] != '<' || trimmed[1] != sigil || trimmed[^1] != '>')
        {
            return default;
        }

        return trimmed[2..^1];
    }

    private static bool TryParseSnowflake(ReadOnlySpan<char> value, out ulong snowflake)
    {
        snowflake = 0uL;

        if (value.IsEmpty)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out snowflake)
            && snowflake != 0uL;
    }
}
