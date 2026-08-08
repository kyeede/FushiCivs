using System.Collections;

namespace Fushi.Core.Utilities.Paging;

/// <summary>
/// One page of items together with its position in the full sequence.
/// </summary>
/// <remarks>
/// Enumerable over its own items, so a caller that only wants to render the
/// contents can <c>foreach</c> the page directly and reach for
/// <see cref="Info"/> only when it needs to draw navigation.
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
public sealed class Page<T> : IReadOnlyList<T>
{
    /// <summary>
    /// Initialises a page from its items and position.
    /// </summary>
    /// <param name="items">The items on this page.</param>
    /// <param name="info">Where the page sits in the full sequence.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="items"/> is <see langword="null"/>.
    /// </exception>
    public Page(IReadOnlyList<T> items, PageInfo info)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = items;
        Info = info;
    }

    /// <summary>
    /// Gets the items on this page.
    /// </summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// Gets where this page sits in the full sequence.
    /// </summary>
    public PageInfo Info { get; }

    /// <inheritdoc/>
    public int Count => Items.Count;

    /// <inheritdoc/>
    public T this[int index] => Items[index];

    /// <summary>
    /// Gets an empty page.
    /// </summary>
    /// <param name="size">The page size that was requested.</param>
    /// <returns>A page with no items.</returns>
    public static Page<T> Empty(int size = PageRequest.DEFAULT_SIZE) => new([], PageInfo.Empty(size));

    /// <summary>
    /// Builds a page from the items of one page and the total item count.
    /// </summary>
    /// <param name="items">The items belonging to the requested page.</param>
    /// <param name="request">The request the items were fetched for.</param>
    /// <param name="totalCount">
    /// The number of items in the whole sequence, ignoring paging.
    /// </param>
    /// <returns>The assembled page.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="items"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="totalCount"/> is negative.
    /// </exception>
    public static Page<T> From(IReadOnlyList<T> items, PageRequest request, int totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new Page<T>(items, new PageInfo(request.Number, request.Size, totalCount));
    }

    /// <summary>
    /// Projects each item onto a new page with the same position.
    /// </summary>
    /// <remarks>
    /// Lets a handler fetch a page of entities and hand back a page of read
    /// models without recomputing or re-querying the position.
    /// </remarks>
    /// <typeparam name="TOut">The projected item type.</typeparam>
    /// <param name="selector">The projection to apply to each item.</param>
    /// <returns>A page of projected items.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="selector"/> is <see langword="null"/>.
    /// </exception>
    public Page<TOut> Map<TOut>(Func<T, TOut> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var projected = new TOut[Items.Count];
        for (int index = 0; index < Items.Count; index++)
        {
            projected[index] = selector(Items[index]);
        }

        return new Page<TOut>(projected, Info);
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public override string ToString() => $"{Count} item(s), {Info}";
}
