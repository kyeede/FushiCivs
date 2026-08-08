namespace Fushi.Core.Entities.Guilds;

/// <summary>
/// The channels a guild has wired the bot into.
/// </summary>
/// <remarks>
/// Grouped into one value rather than spread across five columns on
/// <see cref="Guild"/> so that "is this guild configured yet" is a single
/// question with a single answer, and so that reconfiguring the routing is one
/// atomic replacement instead of five independent writes that can be halfway
/// applied.
/// <br/>
/// Every channel is optional because a guild is created the moment the bot
/// joins, long before an administrator has chosen anything. <see cref="IsReady"/>
/// is what separates a guild that can run a cycle from one that cannot.
/// </remarks>
public readonly record struct GuildChannels
{
    /// <summary>
    /// Initialises the channel routing.
    /// </summary>
    /// <param name="intakeChannelId">
    /// The channel new submissions are collected from.
    /// </param>
    /// <param name="reviewChannelId">
    /// The channel submissions are posted to for voting.
    /// </param>
    /// <param name="resultsChannelId">
    /// The channel outcomes are announced in.
    /// </param>
    /// <param name="archiveChannelId">
    /// The channel decided submissions are copied to for the record.
    /// </param>
    /// <param name="logChannelId">
    /// The channel moderation activity is logged to.
    /// </param>
    public GuildChannels(
        ulong? intakeChannelId = null,
        ulong? reviewChannelId = null,
        ulong? resultsChannelId = null,
        ulong? archiveChannelId = null,
        ulong? logChannelId = null
    )
    {
        IntakeChannelId = Normalise(intakeChannelId);
        ReviewChannelId = Normalise(reviewChannelId);
        ResultsChannelId = Normalise(resultsChannelId);
        ArchiveChannelId = Normalise(archiveChannelId);
        LogChannelId = Normalise(logChannelId);
    }

    /// <summary>
    /// Gets the channel new submissions are collected from.
    /// </summary>
    public ulong? IntakeChannelId { get; init; }

    /// <summary>
    /// Gets the channel submissions are posted to for voting.
    /// </summary>
    public ulong? ReviewChannelId { get; init; }

    /// <summary>
    /// Gets the channel outcomes are announced in.
    /// </summary>
    /// <value>
    /// The results channel, or <see langword="null"/> to announce in
    /// <see cref="ReviewChannelId"/> instead.
    /// </value>
    public ulong? ResultsChannelId { get; init; }

    /// <summary>
    /// Gets the channel decided submissions are copied to for the record.
    /// </summary>
    /// <value>
    /// The archive channel, or <see langword="null"/> to skip archiving.
    /// </value>
    public ulong? ArchiveChannelId { get; init; }

    /// <summary>
    /// Gets the channel moderation activity is logged to.
    /// </summary>
    /// <value>
    /// The log channel, or <see langword="null"/> to keep the audit trail in
    /// the database only.
    /// </value>
    public ulong? LogChannelId { get; init; }

    /// <summary>
    /// Gets a value indicating whether enough routing is configured to run a
    /// cycle.
    /// </summary>
    /// <value>
    /// <see langword="true"/> once both <see cref="IntakeChannelId"/> and
    /// <see cref="ReviewChannelId"/> are set. The remaining channels are
    /// enhancements and have sensible fallbacks.
    /// </value>
    public bool IsReady => IntakeChannelId.HasValue && ReviewChannelId.HasValue;

    /// <summary>
    /// Gets the channel outcomes should actually be announced in.
    /// </summary>
    /// <value>
    /// <see cref="ResultsChannelId"/> when set, otherwise
    /// <see cref="ReviewChannelId"/>, otherwise <see langword="null"/>.
    /// </value>
    public ulong? EffectiveResultsChannelId => ResultsChannelId ?? ReviewChannelId;

    /// <summary>
    /// Gets the channels that are configured, without duplicates.
    /// </summary>
    /// <returns>
    /// The distinct snowflakes of every channel this guild routes through.
    /// </returns>
    public IReadOnlySet<ulong> DistinctChannelIds()
    {
        var channels = new HashSet<ulong>(capacity: 5);

        foreach (
            ulong? channel in (ReadOnlySpan<ulong?>)
                [IntakeChannelId, ReviewChannelId, ResultsChannelId, ArchiveChannelId, LogChannelId]
        )
        {
            if (channel is { } id)
            {
                _ = channels.Add(id);
            }
        }

        return channels;
    }

    // A snowflake of zero is not a channel; treating it as "unset" stops a
    // default-initialised value from looking configured.
    private static ulong? Normalise(ulong? channelId) => channelId is null or 0uL ? null : channelId;
}
