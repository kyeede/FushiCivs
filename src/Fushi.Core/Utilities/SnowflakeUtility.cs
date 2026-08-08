namespace Fushi.Core.Utilities;

/// <summary>
/// Reads the timestamp that Discord embeds in every snowflake identifier.
/// </summary>
/// <remarks>
/// A snowflake is a 64-bit integer whose top 42 bits are the milliseconds since
/// the Discord epoch of 2015-01-01T00:00:00Z. Because creation time is carried
/// inside the identifier, the age of a message, user, or channel is available
/// without asking the API for it.
/// <br/>
/// This is what makes it possible to answer "was this account made yesterday"
/// during submission intake without spending a rate-limited request.
/// </remarks>
/// <seealso href="https://discord.com/developers/docs/reference#snowflakes">
/// Discord developer documentation: Snowflakes
/// </seealso>
public static class SnowflakeUtility
{
    /// <summary>
    /// The first millisecond of 2015 in Unix milliseconds, which is the epoch
    /// all Discord snowflakes count from.
    /// </summary>
    public const long DISCORD_EPOCH_MILLISECONDS = 1_420_070_400_000L;

    /// <summary>
    /// The number of low bits occupied by the worker, process, and increment
    /// fields, which the timestamp sits above.
    /// </summary>
    public const int TIMESTAMP_SHIFT = 22;

    /// <summary>
    /// Gets the earliest instant a snowflake can encode.
    /// </summary>
    public static DateTimeOffset Epoch { get; } = DateTimeOffset.FromUnixTimeMilliseconds(DISCORD_EPOCH_MILLISECONDS);

    /// <summary>
    /// Extracts the creation instant encoded in a snowflake.
    /// </summary>
    /// <param name="snowflake">The Discord identifier to read.</param>
    /// <returns>The creation instant in UTC.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="snowflake"/> is <c>0</c>, which is not a real
    /// identifier and would decode to the epoch itself.
    /// </exception>
    public static DateTimeOffset ToTimestamp(ulong snowflake)
    {
        ArgumentOutOfRangeException.ThrowIfZero(snowflake);

        long milliseconds = (long)(snowflake >> TIMESTAMP_SHIFT) + DISCORD_EPOCH_MILLISECONDS;
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    /// <summary>
    /// Attempts to extract the creation instant encoded in a snowflake.
    /// </summary>
    /// <param name="snowflake">The Discord identifier to read.</param>
    /// <param name="timestamp">
    /// When this method returns <see langword="true"/>, the creation instant in
    /// UTC; otherwise <see cref="DateTimeOffset.MinValue"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the snowflake was non-zero; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryToTimestamp(ulong snowflake, out DateTimeOffset timestamp)
    {
        if (snowflake == 0uL)
        {
            timestamp = DateTimeOffset.MinValue;
            return false;
        }

        timestamp = ToTimestamp(snowflake);
        return true;
    }

    /// <summary>
    /// Builds the smallest snowflake that could have been created at or after
    /// the given instant.
    /// </summary>
    /// <remarks>
    /// The worker, process, and increment bits are zeroed, which produces a
    /// value that sorts immediately before every real identifier from that
    /// millisecond. That is exactly what Discord's <c>before</c> and
    /// <c>after</c> pagination parameters expect, so a date range can be turned
    /// into a snowflake range without a lookup.
    /// </remarks>
    /// <param name="timestamp">The instant to encode.</param>
    /// <returns>A synthetic snowflake usable as a pagination boundary.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timestamp"/> precedes <see cref="Epoch"/>.
    /// </exception>
    public static ulong FromTimestamp(DateTimeOffset timestamp)
    {
        if (timestamp < Epoch)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestamp),
                timestamp,
                $"A snowflake cannot encode an instant before the Discord epoch ({Epoch:O})."
            );
        }

        long offset = timestamp.ToUnixTimeMilliseconds() - DISCORD_EPOCH_MILLISECONDS;
        return (ulong)offset << TIMESTAMP_SHIFT;
    }

    /// <summary>
    /// Determines whether a value could be a Discord snowflake.
    /// </summary>
    /// <remarks>
    /// This is a range check, not a proof of existence. It rejects zero and any
    /// value whose encoded timestamp lies in the future, which catches the
    /// common case of a user pasting an arbitrary number into a command.
    /// </remarks>
    /// <param name="snowflake">The value to test.</param>
    /// <param name="asOf">
    /// The instant to treat as "now". Supplied rather than read from the system
    /// clock so that the check stays deterministic under test.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value is plausible; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool IsPlausible(ulong snowflake, DateTimeOffset asOf) =>
        snowflake != 0uL && ToTimestamp(snowflake) <= asOf;
}
