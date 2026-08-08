namespace Fushi.Core.Utilities.Paging;

/// <summary>
/// Where a page sits within the sequence it was taken from.
/// </summary>
/// <remarks>
/// Carried alongside the items so that a renderer can decide whether to enable
/// the next and previous buttons without a second query, and so that a footer
/// can honestly say "page 2 of 7".
/// </remarks>
/// <seealso cref="Page{T}"/>
public readonly record struct PageInfo
{
    /// <summary>
    /// Initialises the position of a page.
    /// </summary>
    /// <param name="number">The one-based page number.</param>
    /// <param name="size">The number of items on a full page.</param>
    /// <param name="totalCount">
    /// The number of items in the whole sequence, ignoring paging.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="number"/> or <paramref name="size"/> is less than
    /// <c>1</c>, or <paramref name="totalCount"/> is negative.
    /// </exception>
    public PageInfo(int number, int size, int totalCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);

        Number = number;
        Size = size;
        TotalCount = totalCount;
    }

    /// <summary>
    /// Gets the one-based number of this page.
    /// </summary>
    public int Number { get; }

    /// <summary>
    /// Gets the number of items on a full page.
    /// </summary>
    public int Size { get; }

    /// <summary>
    /// Gets the number of items in the whole sequence.
    /// </summary>
    public int TotalCount { get; }

    /// <summary>
    /// Gets the number of pages the sequence divides into.
    /// </summary>
    /// <value>
    /// At least <c>1</c>, so that an empty sequence reads as "page 1 of 1"
    /// rather than the nonsensical "page 1 of 0".
    /// </value>
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Size));

    /// <summary>
    /// Gets a value indicating whether a page precedes this one.
    /// </summary>
    public bool HasPrevious => Number > 1;

    /// <summary>
    /// Gets a value indicating whether a page follows this one.
    /// </summary>
    public bool HasNext => Number < TotalPages;

    /// <summary>
    /// Gets a value indicating whether the sequence contained no items at all.
    /// </summary>
    public bool IsEmpty => TotalCount == 0;

    /// <summary>
    /// Gets the position of an empty result.
    /// </summary>
    /// <param name="size">The page size that was requested.</param>
    /// <returns>Position information describing a single empty page.</returns>
    public static PageInfo Empty(int size = PageRequest.DEFAULT_SIZE)
        => new(1, Math.Clamp(size, 1, PageRequest.MAX_SIZE), 0);

    /// <inheritdoc/>
    public override string ToString() => $"Page {Number} of {TotalPages} ({TotalCount} total)";
}
