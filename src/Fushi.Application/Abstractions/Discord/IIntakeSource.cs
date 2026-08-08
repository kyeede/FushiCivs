using Fushi.Core.Results;

namespace Fushi.Application.Abstractions.Discord;

/// <summary>
/// Reads candidate submissions out of a guild's intake channel.
/// </summary>
/// <remarks>
/// Intake is a pull rather than a push. The bot could subscribe to message events
/// and capture posts as they arrive, but then any message posted while the
/// process was restarting would be lost for good. Re-reading the channel from a
/// recorded position instead means a restart costs nothing.
/// </remarks>
public interface IIntakeSource
{
    /// <summary>
    /// Reads messages posted after a known point.
    /// </summary>
    /// <param name="channelId">The intake channel to read.</param>
    /// <param name="afterMessageId">
    /// The last message already processed, or <see langword="null"/> to start
    /// from the oldest available. Passing a snowflake rather than a timestamp
    /// uses Discord's own pagination, which is both cheaper and exact.
    /// </param>
    /// <param name="limit">
    /// The most to read in one pass. Discord caps a single history request at
    /// 100.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>
    /// The messages found, oldest first, or a failure when the channel is gone
    /// or unreadable.
    /// </returns>
    Task<Result<IReadOnlyList<IntakeMessage>>> ReadAsync(
        ulong channelId,
        ulong? afterMessageId,
        int limit,
        CancellationToken cancellationToken = default);
}
