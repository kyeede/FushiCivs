using Fushi.Core.Entities.Cycles;

namespace Fushi.Core.Tests.Entities.Cycles;

/// <summary>
/// Covers <see cref="CycleWindow"/>: the half-open containment rule, the
/// boundary predicates, remaining time, and the guard that a window must close
/// after it opens.
/// </summary>
public sealed class CycleWindowTests
{
    [Fact]
    public void AWindowMustCloseAfterItOpens()
    {
        DateTimeOffset opens = Instant(10);

        _ = Should.Throw<ArgumentOutOfRangeException>(() => new CycleWindow(new DateOnly(2026, 8, 8), opens, opens));
        _ = Should.Throw<ArgumentOutOfRangeException>(
            () => new CycleWindow(new DateOnly(2026, 8, 8), opens, opens.AddSeconds(-1)));
    }

    [Fact]
    public void DurationIsTheDistanceBetweenTheResolvedBoundaries()
    {
        CycleWindow window = Window();

        window.Duration.ShouldBe(TimeSpan.FromHours(12));
    }

    // Half-open: the opening instant is inside, the closing instant is not.
    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(15, true)]
    [InlineData(22, false)]
    [InlineData(23, false)]
    public void ContainsTreatsTheIntervalAsHalfOpen(int hour, bool expected)
    {
        Window().Contains(Instant(hour)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(23, true)]
    public void HasOpenedIsTrueFromTheOpeningInstantOnwards(int hour, bool expected)
    {
        Window().HasOpened(Instant(hour)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(21, false)]
    [InlineData(22, true)]
    [InlineData(23, true)]
    public void HasClosedIsTrueFromTheClosingInstantOnwards(int hour, bool expected)
    {
        Window().HasClosed(Instant(hour)).ShouldBe(expected);
    }

    [Fact]
    public void RemainingFromCountsDownToTheClosingInstant()
    {
        Window().RemainingFrom(Instant(20)).ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void RemainingFromNeverGoesNegative()
    {
        Window().RemainingFrom(Instant(22)).ShouldBe(TimeSpan.Zero);
        Window().RemainingFrom(Instant(23)).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void TheDateLabelIsTheGuildsLocalDateRatherThanTheUtcOne()
    {
        var window = new CycleWindow(
            new DateOnly(2026, 8, 8),
            new DateTimeOffset(2026, 8, 8, 22, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 8, 9, 6, 0, 0, TimeSpan.FromHours(2)));

        window.Date.ShouldBe(new DateOnly(2026, 8, 8));
        window.OpensAt.UtcDateTime.Date.ShouldBe(new DateTime(2026, 8, 8));
        window.ClosesAt.UtcDateTime.Date.ShouldBe(new DateTime(2026, 8, 9));
    }

    private static CycleWindow Window()
        => new(new DateOnly(2026, 8, 8), Instant(10), Instant(22));

    private static DateTimeOffset Instant(int hour) => new(2026, 8, 8, hour, 0, 0, TimeSpan.Zero);
}
