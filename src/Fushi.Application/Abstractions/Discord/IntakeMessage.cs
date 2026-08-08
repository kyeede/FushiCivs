namespace Fushi.Application.Abstractions.Discord;

/// <summary>
/// A message read from a guild's intake channel, reduced to the parts a
/// submission is built from.
/// </summary>
/// <remarks>
/// A deliberately narrow projection of a Discord message. Passing the library's
/// own message type across this boundary would make the whole application layer
/// depend on Discord.Net, and would hand handlers a hundred properties when they
/// need six.
/// </remarks>
/// <param name="ChannelId">The intake channel the message was posted in.</param>
/// <param name="MessageId">
/// The message snowflake, used to recognise a message that has already been
/// captured.
/// </param>
/// <param name="AuthorId">The posting user's snowflake.</param>
/// <param name="Content">
/// The message text, already stripped of anything the reader chose to exclude.
/// </param>
/// <param name="PostedAt">
/// When the message was posted, taken from the snowflake rather than from a
/// separate field.
/// </param>
/// <param name="IsFromBot">
/// Whether a bot posted it. Intake skips these, so the bot cannot capture its own
/// announcements as submissions.
/// </param>
/// <param name="AttachmentUrls">
/// Links to any attachments, preserved in the submission body so that images
/// referenced by an application remain reachable.
/// </param>
public sealed record IntakeMessage(
    ulong ChannelId,
    ulong MessageId,
    ulong AuthorId,
    string Content,
    DateTimeOffset PostedAt,
    bool IsFromBot,
    IReadOnlyList<string> AttachmentUrls)
{
    /// <summary>
    /// Gets a value indicating whether this message could become a submission.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when a human posted it and it has some text.
    /// A message with only an attachment and no words is not rejected outright
    /// here, but it carries nothing that could become a title.
    /// </value>
    public bool IsCandidate => !IsFromBot && !string.IsNullOrWhiteSpace(Content);
}
