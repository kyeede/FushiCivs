using Fushi.Core.Utilities.Paging;

namespace Fushi.Core.Tests.Utilities.Paging;

/// <summary>
/// Covers <see cref="PageInfo"/>: how a total count rounds into a page count,
/// the edges at which the navigation flags flip, and the empty sequence.
/// </summary>
public sealed class PageInfoTests
{
    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(20, 10, 2)]
    [InlineData(21, 10, 3)]
    [InlineData(7, 3, 3)]
    [InlineData(1, 1, 1)]
    public void APartialLastPageStillCounts(int totalCount, int size, int expected)
    {
        new PageInfo(1, size, totalCount).TotalPages.ShouldBe(expected);
    }

    // "Page 1 of 0" is not a thing anybody can read, so an empty sequence is still
    // one page — an empty one.
    [Fact]
    public void AnEmptySequenceIsOnePageRatherThanNone()
    {
        PageInfo info = PageInfo.Empty();

        info.TotalCount.ShouldBe(0);
        info.TotalPages.ShouldBe(1);
        info.Number.ShouldBe(1);
        info.Size.ShouldBe(PageRequest.DEFAULT_SIZE);
        info.IsEmpty.ShouldBeTrue();
        info.HasPrevious.ShouldBeFalse();
        info.HasNext.ShouldBeFalse();
    }

    [Fact]
    public void AnEmptyPositionClampsAnUnreasonableSize()
    {
        PageInfo.Empty(0).Size.ShouldBe(1);
        PageInfo.Empty(-5).Size.ShouldBe(1);
        PageInfo.Empty(PageRequest.MAX_SIZE + 1).Size.ShouldBe(PageRequest.MAX_SIZE);
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(2, true, true)]
    [InlineData(3, true, false)]
    public void TheNavigationFlagsFlipAtTheFirstAndLastPage(int number, bool hasPrevious, bool hasNext)
    {
        PageInfo info = new(number, size: 10, totalCount: 25);

        info.TotalPages.ShouldBe(3);
        info.HasPrevious.ShouldBe(hasPrevious);
        info.HasNext.ShouldBe(hasNext);
    }

    // Asking for a page past the end is answered honestly rather than pretending
    // there is more to come.
    [Fact]
    public void APageBeyondTheEndReportsNothingFollowingIt()
    {
        PageInfo info = new(99, size: 10, totalCount: 25);

        info.HasNext.ShouldBeFalse();
        info.HasPrevious.ShouldBeTrue();
    }

    [Fact]
    public void TheSingleFullPageCaseHasNoNeighbours()
    {
        PageInfo info = new(1, size: 10, totalCount: 10);

        info.TotalPages.ShouldBe(1);
        info.HasPrevious.ShouldBeFalse();
        info.HasNext.ShouldBeFalse();
        info.IsEmpty.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(-1, 10, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 10, -1)]
    public void ConstructionRejectsAPositionThatCannotExist(int number, int size, int totalCount)
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(() => new PageInfo(number, size, totalCount));
    }

    [Fact]
    public void TwoPositionsDescribingTheSamePageAreEqual()
    {
        new PageInfo(2, 10, 25).ShouldBe(new PageInfo(2, 10, 25));
        new PageInfo(2, 10, 25).ShouldNotBe(new PageInfo(3, 10, 25));
    }
}
