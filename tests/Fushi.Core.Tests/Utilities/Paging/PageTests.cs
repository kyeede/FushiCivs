using Fushi.Core.Utilities.Paging;

namespace Fushi.Core.Tests.Utilities.Paging;

/// <summary>
/// Covers <see cref="Page{T}"/>: assembling a page from the items fetched for a
/// request, projecting one onto another without disturbing its position, and
/// the empty page.
/// </summary>
public sealed class PageTests
{
    [Fact]
    public void AssemblingAPageTakesItsPositionFromTheRequestAndTheTotal()
    {
        Page<string> page = Page<string>.From(["d", "e", "f"], new PageRequest(2, 3), totalCount: 8);

        page.Count.ShouldBe(3);
        page.Info.Number.ShouldBe(2);
        page.Info.Size.ShouldBe(3);
        page.Info.TotalCount.ShouldBe(8);
        page.Info.TotalPages.ShouldBe(3);
        page.Info.HasNext.ShouldBeTrue();
    }

    [Fact]
    public void APageIsEnumerableAndIndexableOverItsOwnItems()
    {
        Page<int> page = Page<int>.From([1, 2, 3], PageRequest.Default, totalCount: 3);

        page[0].ShouldBe(1);
        page[2].ShouldBe(3);
        page.ShouldBe([1, 2, 3]);
        page.Items.ShouldBe([1, 2, 3]);
    }

    // The point of projecting rather than rebuilding: a handler can turn a page of
    // entities into a page of read models without recounting the sequence.
    [Fact]
    public void ProjectingAPageKeepsItsPositionExactly()
    {
        Page<int> page = Page<int>.From([1, 2, 3], new PageRequest(2, 3), totalCount: 8);

        Page<string> projected = page.Map(value => new string('x', value));

        projected.ShouldBe(["x", "xx", "xxx"]);
        projected.Info.ShouldBe(page.Info);
    }

    [Fact]
    public void ProjectingAnEmptyPageProducesAnEmptyPage()
    {
        Page<string> projected = Page<int>.Empty().Map(value => new string('x', value));

        projected.Count.ShouldBe(0);
        projected.Info.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void AnEmptyPageIsTheFirstPageOfNothing()
    {
        Page<string> page = Page<string>.Empty();

        page.Count.ShouldBe(0);
        page.Info.Number.ShouldBe(1);
        page.Info.TotalPages.ShouldBe(1);
        page.Info.Size.ShouldBe(PageRequest.DEFAULT_SIZE);
    }

    [Fact]
    public void AnEmptyPageRemembersTheSizeThatWasAskedFor()
    {
        Page<string>.Empty(25).Info.Size.ShouldBe(25);
    }

    // The last page of a sequence is shorter than the page size, and that has to
    // stay true of the items without changing what the position reports.
    [Fact]
    public void AShortLastPageStillReportsTheFullSize()
    {
        Page<int> page = Page<int>.From([9], new PageRequest(3, 4), totalCount: 9);

        page.Count.ShouldBe(1);
        page.Info.Size.ShouldBe(4);
        page.Info.HasNext.ShouldBeFalse();
    }

    [Fact]
    public void APageCannotBeBuiltWithoutItems()
    {
        _ = Should.Throw<ArgumentNullException>(() => new Page<int>(null!, PageInfo.Empty()));
        _ = Should.Throw<ArgumentNullException>(() => Page<int>.From(null!, PageRequest.Default, 0));
        _ = Should.Throw<ArgumentNullException>(() => Page<int>.Empty().Map<int>(null!));
    }

    [Fact]
    public void ANegativeTotalCannotDescribeAPage()
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(
            () => Page<int>.From([], PageRequest.Default, totalCount: -1));
    }
}
