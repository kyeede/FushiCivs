using Fushi.Core.Utilities.Paging;

namespace Fushi.Core.Extensions;

/// <summary>
/// Sequence helpers used across the layers, written as C# extension members.
/// </summary>
/// <remarks>
/// These operate on in-memory sequences. A queryable source should page in the
/// database instead, so that the rows outside the page are never transferred;
/// the persistence layer provides its own overloads for that.
/// </remarks>
public static class EnumerableExtensions
{
    extension<T>(IEnumerable<T> source)
    {
        /// <summary>
        /// Takes one page out of an in-memory sequence.
        /// </summary>
        /// <remarks>
        /// Enumerates <paramref name="source"/> once into a list to count it,
        /// so it is safe to call on a sequence that cannot be enumerated twice.
        /// </remarks>
        /// <param name="request">The page to take.</param>
        /// <returns>
        /// The requested page. Requesting a page past the end yields a page
        /// with no items but with an accurate total count, which lets a
        /// renderer say "page 9 of 3" rather than silently showing the last
        /// page.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The source sequence is <see langword="null"/>.
        /// </exception>
        public Page<T> ToPage(PageRequest request)
        {
            ArgumentNullException.ThrowIfNull(source);

            List<T> all = [.. source];
            List<T> items = [.. all.Skip(request.Skip).Take(request.Size)];

            return Page<T>.From(items, request, all.Count);
        }

        /// <summary>
        /// Filters out the elements that are <see langword="null"/>, and tells
        /// the compiler that the remainder are not.
        /// </summary>
        /// <returns>The non-null elements, in their original order.</returns>
        /// <exception cref="ArgumentNullException">
        /// The source sequence is <see langword="null"/>.
        /// </exception>
        public IEnumerable<T> WhereNotNull()
        {
            ArgumentNullException.ThrowIfNull(source);

            foreach (T? item in source)
            {
                if (item is not null)
                {
                    yield return item;
                }
            }
        }

        /// <summary>
        /// Determines whether the sequence contains no elements, treating a
        /// <see langword="null"/> sequence as empty.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when the sequence is <see langword="null"/>
        /// or yields nothing; otherwise <see langword="false"/>.
        /// </returns>
        public bool IsNullOrEmpty() => source is null || !source.Any();
    }
}
