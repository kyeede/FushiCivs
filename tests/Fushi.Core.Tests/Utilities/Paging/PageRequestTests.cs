using Fushi.Core.Utilities.Paging;

namespace Fushi.Core.Tests.Utilities.Paging;

/// <summary>
/// Covers <see cref="PageRequest"/>: its defaults, the read-side normalisation of
/// an unconstructed value, clamping of untrusted input, and the offset arithmetic.
/// </summary>
public sealed class PageRequestTests
{
    // Written as `new()`, this produced page 0 at 0 items per page. Nothing threw:
    // the request reached the database as Take(0) and the caller got an empty page,
    // which is indistinguishable from having no data.
    [Fact]
    public void TheDefaultRequestAsksForTheFirstPageAtTheDefaultSize()
    {
        PageRequest.Default.Number.ShouldBe(1);
        PageRequest.Default.Size.ShouldBe(PageRequest.DEFAULT_SIZE);
        PageRequest.Default.Skip.ShouldBe(0);
    }

    // A PageRequest can reach a handler without ever running its constructor — as
    // an unset field on a query record, for instance. Zero is not a valid value for
    // either property, so reading it as the default costs nothing and removes a
    // whole class of empty-result bug.
    [Fact]
    public void AnUnconstructedRequestReadsAsTheDefaultRatherThanAsZero()
    {
        PageRequest unconstructed = default;

        unconstructed.Number.ShouldBe(1);
        unconstructed.Size.ShouldBe(PageRequest.DEFAULT_SIZE);
        unconstructed.Skip.ShouldBe(0);
    }

    [Theory]
    [InlineData(1, 10, 0)]
    [InlineData(2, 10, 10)]
    [InlineData(3, 25, 50)]
    public void SkipIsTheOffsetOfTheFirstItemOnThePage(int number, int size, int expected)
    {
        new PageRequest(number, size).Skip.ShouldBe(expected);
    }

    // Computed in long arithmetic, so a large page number saturates instead of
    // overflowing into a negative offset the database would reject.
    [Fact]
    public void SkipSaturatesRatherThanOverflowingOnAnAbsurdPageNumber()
    {
        new PageRequest(int.MaxValue, PageRequest.MAX_SIZE).Skip.ShouldBe(int.MaxValue);
    }

    [Theory]
    [InlineData(0, 10, 1, 10)]
    [InlineData(-5, 10, 1, 10)]
    [InlineData(4, 0, 4, 1)]
    [InlineData(4, 1000, 4, PageRequest.MAX_SIZE)]
    public void ClampCorrectsOutOfRangeInputInsteadOfRejectingIt(
        int number,
        int size,
        int expectedNumber,
        int expectedSize)
    {
        PageRequest request = PageRequest.Clamp(number, size);

        request.Number.ShouldBe(expectedNumber);
        request.Size.ShouldBe(expectedSize);
    }

    // Clamp is for values a person typed. The constructor is for values a
    // programmer supplied, where out of range means a bug rather than a typo.
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, PageRequest.MAX_SIZE + 1)]
    public void TheConstructorRefusesOutOfRangeInput(int number, int size)
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(() => new PageRequest(number, size));
    }
}
