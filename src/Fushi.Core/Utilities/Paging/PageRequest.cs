namespace Fushi.Core.Utilities.Paging;

/// <summary>
/// A request for one page of a larger sequence.
/// </summary>
/// <remarks>
/// Page numbers are one-based because this value travels straight from a
/// command a person typed, and <c>page:1</c> is the first page to everyone
/// except a programmer. The zero-based offset the database needs is derived in
/// <see cref="Skip"/> rather than pushed onto the caller.
/// </remarks>
/// <seealso cref="Page{T}"/>
public readonly record struct PageRequest
{
    /// <summary>
    /// The page size used when a caller does not specify one, chosen to fit a
    /// readable Discord embed without scrolling.
    /// </summary>
    public const int DEFAULT_SIZE = 10;

    /// <summary>
    /// The largest page size accepted, bounding the work a single command can
    /// ask the database and the renderer to do.
    /// </summary>
    public const int MAX_SIZE = 100;

    /// <summary>
    /// Initialises a request for a specific page.
    /// </summary>
    /// <param name="number">The one-based page number.</param>
    /// <param name="size">The number of items per page.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="number"/> is less than <c>1</c>, or
    /// <paramref name="size"/> is outside the range <c>1</c> to
    /// <see cref="MAX_SIZE"/>.
    /// </exception>
    public PageRequest(int number = 1, int size = DEFAULT_SIZE)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MAX_SIZE);

        Number = number;
        Size = size;
    }

    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    /// <remarks>
    /// Reads a zero as the first page. The constructor rejects anything below
    /// <c>1</c>, so a zero here can only have come from a struct that was never
    /// constructed — <c>default(PageRequest)</c>, or a record field left unset —
    /// and there is no sensible reading of it other than the first page.
    /// </remarks>
    public int Number => field is 0 ? 1 : field;

    /// <summary>
    /// Gets the number of items on a full page.
    /// </summary>
    /// <remarks>
    /// Reads a zero as <see cref="DEFAULT_SIZE"/>, for the same reason as
    /// <see cref="Number"/>. This one matters more: an unnormalised zero reaches
    /// the database as <c>Take(0)</c>, and the caller gets an empty page rather
    /// than an error, which looks exactly like having no data.
    /// </remarks>
    public int Size => field is 0 ? DEFAULT_SIZE : field;

    /// <summary>
    /// Gets the number of items to skip to reach the start of this page.
    /// </summary>
    /// <remarks>
    /// Computed in <see cref="long"/> arithmetic and clamped, because a
    /// sufficiently large page number would otherwise overflow into a negative
    /// offset and turn a harmless bad input into a query error.
    /// </remarks>
    public int Skip => (int)Math.Min((long)(Number - 1) * Size, int.MaxValue);

    /// <summary>
    /// Gets the default request: the first page at
    /// <see cref="DEFAULT_SIZE"/> items.
    /// </summary>
    /// <remarks>
    /// The arguments are stated rather than left implicit because <c>new()</c> on
    /// a struct binds to the implicit zero-initialising constructor, not to the
    /// one whose parameters merely all have defaults. The getters above would
    /// cover it, but a default page size should not depend on a fallback several
    /// lines away to be correct.
    /// </remarks>
    public static PageRequest Default => new(1, DEFAULT_SIZE);

    /// <summary>
    /// Builds a request from untrusted input, correcting out-of-range values
    /// instead of rejecting them.
    /// </summary>
    /// <remarks>
    /// Intended for values arriving from a slash command, where answering
    /// <c>page:0</c> with the first page is more useful than answering with an
    /// error. Use the constructor where an out-of-range value would indicate a
    /// bug rather than a typo.
    /// </remarks>
    /// <param name="number">The requested page number, clamped to at least <c>1</c>.</param>
    /// <param name="size">
    /// The requested page size, clamped to the range <c>1</c> to
    /// <see cref="MAX_SIZE"/>.
    /// </param>
    /// <returns>A valid request.</returns>
    public static PageRequest Clamp(int number, int size = DEFAULT_SIZE)
        => new(Math.Max(number, 1), Math.Clamp(size, 1, MAX_SIZE));

    /// <inheritdoc/>
    public override string ToString() => $"Page {Number} ({Size} per page)";
}
