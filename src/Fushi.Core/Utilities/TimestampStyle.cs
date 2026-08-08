namespace Fushi.Core.Utilities;

/// <summary>
/// How Discord should render a timestamp embedded in message content.
/// </summary>
/// <remarks>
/// Discord renders these client-side in each viewer's own locale and time zone.
/// Writing "voting closes at 22:00 CEST" hard-codes an assumption about where
/// the reader is; emitting a styled timestamp instead means everyone sees the
/// same instant expressed in their own terms, which matters for a server whose
/// schedule is defined in one European zone but whose members are not.
/// </remarks>
/// <seealso href="https://discord.com/developers/docs/reference#message-formatting-timestamp-styles">
/// Discord developer documentation: Timestamp styles
/// </seealso>
public enum TimestampStyle
{
    /// <summary>
    /// Short time, such as <c>16:20</c>.
    /// </summary>
    ShortTime = 0,

    /// <summary>
    /// Long time, such as <c>16:20:30</c>.
    /// </summary>
    LongTime = 1,

    /// <summary>
    /// Short date, such as <c>20/04/2026</c>.
    /// </summary>
    ShortDate = 2,

    /// <summary>
    /// Long date, such as <c>20 April 2026</c>.
    /// </summary>
    LongDate = 3,

    /// <summary>
    /// Long date with short time, such as <c>20 April 2026 16:20</c>.
    /// </summary>
    ShortDateTime = 4,

    /// <summary>
    /// Day of week, long date, and short time, such as
    /// <c>Monday, 20 April 2026 16:20</c>.
    /// </summary>
    LongDateTime = 5,

    /// <summary>
    /// Time relative to now, such as <c>in 2 hours</c>, updating live as the
    /// reader watches.
    /// </summary>
    Relative = 6,
}
