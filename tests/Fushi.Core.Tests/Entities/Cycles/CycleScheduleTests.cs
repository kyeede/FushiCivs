using Fushi.Core.Entities.Cycles;

namespace Fushi.Core.Tests.Entities.Cycles;

/// <summary>
/// Covers <see cref="CycleSchedule"/>: the default cadence, resolution of a
/// wall-clock rule onto absolute instants across daylight saving transitions,
/// window lookup around the opening and closing boundaries, overnight windows,
/// and time zone resolution.
/// </summary>
public sealed class CycleScheduleTests
{
    private const string BERLIN = CycleSchedule.DEFAULT_TIME_ZONE_ID;

    [Fact]
    public void AConfiguredScheduleKeepsItsDaysTimesAndZone()
    {
        CycleSchedule schedule = Standard();

        schedule.Days.ShouldBe(CycleDays.Monday | CycleDays.Wednesday | CycleDays.Saturday);
        schedule.OpensAt.ShouldBe(new TimeOnly(10, 0));
        schedule.ClosesAt.ShouldBe(new TimeOnly(22, 0));
        schedule.TimeZoneId.ShouldBe("Europe/Berlin");
        schedule.IsOvernight.ShouldBeFalse();
    }

    [Fact]
    public void TheOpeningAndClosingTimesFallBackToTenAndTwentyTwoWhenUnset()
    {
        var schedule = new CycleSchedule(CycleDays.Daily);

        schedule.OpensAt.ShouldBe(new TimeOnly(10, 0));
        schedule.ClosesAt.ShouldBe(new TimeOnly(22, 0));
    }

    // A schedule materialised straight from a database row never runs the
    // constructor, so the times and the zone have to fall back on read.
    [Fact]
    public void AScheduleBuiltWithoutItsConstructorStillReportsTheDocumentedTimesAndZone()
    {
        CycleSchedule materialised = default;

        materialised.OpensAt.ShouldBe(new TimeOnly(10, 0));
        materialised.ClosesAt.ShouldBe(new TimeOnly(22, 0));
        materialised.TimeZoneId.ShouldBe("Europe/Berlin");
    }

    // Regression guard for a defect this suite found. Default was written as
    // `new()`, which for a struct binds to the implicit parameterless constructor
    // rather than to the one whose parameters merely all have defaults — so the
    // cadence was silently lost. Guild's constructor assigns this value, so every
    // newly joined guild accepted submissions and never opened a cycle to vote on
    // them. Anyone tempted to simplify Default back to `new()` should fail here.
    [Fact]
    public void DefaultScheduleKeepsTheDocumentedCadenceAndNotJustTheDocumentedTimes()
    {
        CycleSchedule.Default.Days.ShouldBe(CycleDays.Standard);

        CycleSchedule.Default.OpensAt.ShouldBe(new TimeOnly(10, 0));
        CycleSchedule.Default.ClosesAt.ShouldBe(new TimeOnly(22, 0));
        CycleSchedule.Default.TimeZoneId.ShouldBe("Europe/Berlin");
    }

    // The property assertions above would all still pass on a schedule with no
    // days, because they only cover the values that do fall back on read. This is
    // the one that actually distinguishes a working default from a dormant one.
    [Fact]
    public void DefaultScheduleOpensAWindowOnItsCycleDays()
    {
        // A Monday, and one of the three days the standard cadence names.
        var monday = new DateOnly(2026, 8, 10);

        CycleSchedule.Default.WindowFor(monday).ShouldNotBeNull();
        CycleSchedule.Default.WindowFor(monday.AddDays(1)).ShouldBeNull();
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, true)]
    [InlineData(DayOfWeek.Tuesday, false)]
    [InlineData(DayOfWeek.Wednesday, true)]
    [InlineData(DayOfWeek.Thursday, false)]
    [InlineData(DayOfWeek.Friday, false)]
    [InlineData(DayOfWeek.Saturday, true)]
    [InlineData(DayOfWeek.Sunday, false)]
    public void IncludesAgreesWithTheConfiguredDays(DayOfWeek day, bool expected)
    {
        Standard().Includes(day).ShouldBe(expected);
        new CycleSchedule(CycleDays.Daily).Includes(day).ShouldBeTrue();
        new CycleSchedule(CycleDays.None).Includes(day).ShouldBeFalse();
    }

    [Theory]
    [InlineData(2026, 8, 8, true)]
    [InlineData(2026, 8, 9, false)]
    [InlineData(2026, 8, 10, true)]
    [InlineData(2026, 8, 11, false)]
    [InlineData(2026, 8, 12, true)]
    public void IsCycleDayAgreesWithIncludes(int year, int month, int day, bool expected)
    {
        var date = new DateOnly(year, month, day);

        Standard().IsCycleDay(date).ShouldBe(expected);
        Standard().IsCycleDay(date).ShouldBe(Standard().Includes(date.DayOfWeek));
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, CycleDays.Monday)]
    [InlineData(DayOfWeek.Tuesday, CycleDays.Tuesday)]
    [InlineData(DayOfWeek.Wednesday, CycleDays.Wednesday)]
    [InlineData(DayOfWeek.Thursday, CycleDays.Thursday)]
    [InlineData(DayOfWeek.Friday, CycleDays.Friday)]
    [InlineData(DayOfWeek.Saturday, CycleDays.Saturday)]
    [InlineData(DayOfWeek.Sunday, CycleDays.Sunday)]
    public void FlagForMapsEachDayOntoItsOwnBit(DayOfWeek day, CycleDays expected) => CycleSchedule.FlagFor(day).ShouldBe(expected);

    [Fact]
    public void WindowForReturnsNothingOnADayTheScheduleDoesNotRunOn()
    {
        SkipWithoutBerlin();

        Standard().WindowFor(new DateOnly(2026, 8, 9)).ShouldBeNull();
    }

    [Fact]
    public void WindowForResolvesTheWallClockRuleOntoAbsoluteInstants()
    {
        SkipWithoutBerlin();

        CycleWindow window = Resolved(Standard().WindowFor(new DateOnly(2026, 8, 8)));

        window.Date.ShouldBe(new DateOnly(2026, 8, 8));
        window.OpensAt.ShouldBe(new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero));
        window.ClosesAt.ShouldBe(new DateTimeOffset(2026, 8, 8, 20, 0, 0, TimeSpan.Zero));
        window.Duration.ShouldBe(TimeSpan.FromHours(12));
    }

    // Summer 10:00 Berlin is 08:00 UTC; winter 10:00 Berlin is 09:00 UTC. A
    // schedule stored as a fixed offset would use the same UTC hour year-round
    // and silently shift every winter cycle by an hour.
    [Fact]
    public void TheUtcInstantOfTheSameWallClockDiffersBetweenSummerAndWinter()
    {
        SkipWithoutBerlin();

        var schedule = new CycleSchedule(CycleDays.Daily);

        CycleWindow summer = Resolved(schedule.WindowFor(new DateOnly(2026, 7, 4)));
        CycleWindow winter = Resolved(schedule.WindowFor(new DateOnly(2026, 1, 3)));

        summer.OpensAt.ShouldBe(new DateTimeOffset(2026, 7, 4, 8, 0, 0, TimeSpan.Zero));
        winter.OpensAt.ShouldBe(new DateTimeOffset(2026, 1, 3, 9, 0, 0, TimeSpan.Zero));
        summer.OpensAt.UtcDateTime.TimeOfDay.ShouldNotBe(winter.OpensAt.UtcDateTime.TimeOfDay);
    }

    // The clocks move at 02:00 local on the last Sunday of March and October, so
    // a 10:00-22:00 window sits wholly on one side of the transition and keeps
    // its twelve hours of wall-clock time on both days.
    [Theory]
    [InlineData(2026, 3, 29)]
    [InlineData(2026, 10, 25)]
    public void AWindowClearOfTheTransitionKeepsItsTwelveWallClockHours(int year, int month, int day)
    {
        SkipWithoutBerlin();

        CycleWindow window = Resolved(new CycleSchedule(CycleDays.Daily).WindowFor(new DateOnly(year, month, day)));

        window.Duration.ShouldBe(TimeSpan.FromHours(12));
        window.OpensAt.Offset.ShouldBe(TimeSpan.Zero);
        window.ClosesAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    // A window straddling the spring-forward transition is an hour shorter than
    // the clock says, because the hour between 02:00 and 03:00 never happens.
    [Fact]
    public void AWindowStraddlingTheSpringForwardTransitionLosesAnHour()
    {
        SkipWithoutBerlin();

        var schedule = new CycleSchedule(CycleDays.Daily, new TimeOnly(1, 0), new TimeOnly(5, 0));

        CycleWindow window = Resolved(schedule.WindowFor(new DateOnly(2026, 3, 29)));

        // 01:00 CET → 00:00 UTC; 05:00 CEST → 03:00 UTC.
        window.OpensAt.ShouldBe(new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.Zero));
        window.ClosesAt.ShouldBe(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.Zero));
        window.Duration.ShouldBe(TimeSpan.FromHours(3));
    }

    // The mirror image: the hour between 02:00 and 03:00 happens twice in
    // October, so the same wall-clock window runs an hour longer.
    [Fact]
    public void AWindowStraddlingTheAutumnBackTransitionGainsAnHour()
    {
        SkipWithoutBerlin();

        var schedule = new CycleSchedule(CycleDays.Daily, new TimeOnly(1, 0), new TimeOnly(5, 0));

        CycleWindow window = Resolved(schedule.WindowFor(new DateOnly(2026, 10, 25)));

        // 01:00 CEST → 23:00 UTC on the 24th; 05:00 CET → 04:00 UTC on the 25th.
        window.OpensAt.ShouldBe(new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero));
        window.ClosesAt.ShouldBe(new DateTimeOffset(2026, 10, 25, 4, 0, 0, TimeSpan.Zero));
        window.Duration.ShouldBe(TimeSpan.FromHours(5));
    }

    // 02:30 does not exist on the day the clocks go forward. Resolution steps to
    // the first instant that does, rather than throwing at a guild that
    // configured a perfectly reasonable opening time.
    [Fact]
    public void AnOpeningTimeTheSpringForwardTransitionSkipsMovesToTheFirstRealInstant()
    {
        SkipWithoutBerlin();

        var schedule = new CycleSchedule(CycleDays.Daily, new TimeOnly(2, 30), new TimeOnly(22, 0));

        CycleWindow window = Resolved(schedule.WindowFor(new DateOnly(2026, 3, 29)));

        // 03:00 CEST → 01:00 UTC.
        window.OpensAt.ShouldBe(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero));
    }

    // 02:30 happens twice on the day the clocks go back. Opening takes the
    // earlier of the two instants, so the window is never shortened.
    [Fact]
    public void AnAmbiguousOpeningTimeTakesTheEarlierOfTheTwoInstants()
    {
        SkipWithoutBerlin();

        var schedule = new CycleSchedule(CycleDays.Daily, new TimeOnly(2, 30), new TimeOnly(22, 0));

        CycleWindow window = Resolved(schedule.WindowFor(new DateOnly(2026, 10, 25)));

        window.OpensAt.Offset.ShouldBe(TimeSpan.Zero);
        window.OpensAt.ShouldBe(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));
    }

    // Closing takes the later of the two, for the same reason from the other
    // end: an overnight window ending at 02:30 on the transition day runs an
    // hour longer rather than being clipped.
    [Fact]
    public void AnAmbiguousClosingTimeTakesTheLaterOfTheTwoInstants()
    {
        SkipWithoutBerlin();

        var schedule = new CycleSchedule(CycleDays.Daily, new TimeOnly(22, 0), new TimeOnly(2, 30));

        CycleWindow window = Resolved(schedule.WindowFor(new DateOnly(2026, 10, 24)));

        window.ClosesAt.Offset.ShouldBe(TimeSpan.Zero);
        window.ClosesAt.ShouldBe(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero));
        window.Duration.ShouldBe(TimeSpan.FromHours(5.5));
    }

    [Fact]
    public void CurrentWindowIsNothingJustBeforeOpening()
    {
        SkipWithoutBerlin();

        Standard().CurrentWindow(Instant(7, 59, 59)).ShouldBeNull();
    }

    [Fact]
    public void CurrentWindowIsTheOpenOneFromTheOpeningInstantOnwards()
    {
        SkipWithoutBerlin();

        CycleWindow atOpening = Resolved(Standard().CurrentWindow(Instant(8, 0, 0)));
        CycleWindow midway = Resolved(Standard().CurrentWindow(Instant(14, 0, 0)));

        atOpening.Date.ShouldBe(new DateOnly(2026, 8, 8));
        midway.ShouldBe(atOpening);
    }

    // The interval is half-open, so a vote cast at exactly the closing instant is
    // late. Anything else would let two adjacent windows claim the same instant.
    [Fact]
    public void CurrentWindowIsNothingAtExactlyTheClosingInstant()
    {
        SkipWithoutBerlin();

        Standard().CurrentWindow(Instant(19, 59, 59)).ShouldNotBeNull();
        Standard().CurrentWindow(Instant(20, 0, 0)).ShouldBeNull();
    }

    [Fact]
    public void CurrentWindowIsNothingAfterClosing()
    {
        SkipWithoutBerlin();

        Standard().CurrentWindow(Instant(22, 0, 0)).ShouldBeNull();
    }

    [Fact]
    public void NextWindowIsTodaysWindowBeforeItOpens()
    {
        SkipWithoutBerlin();

        Resolved(Standard().NextWindow(Instant(6, 0, 0))).Date.ShouldBe(new DateOnly(2026, 8, 8));
    }

    [Fact]
    public void NextWindowReportsTheWindowInProgressRatherThanSkippingPastIt()
    {
        SkipWithoutBerlin();

        Resolved(Standard().NextWindow(Instant(12, 0, 0))).Date.ShouldBe(new DateOnly(2026, 8, 8));
    }

    [Fact]
    public void NextWindowMovesToTheFollowingCycleDayOnceTodaysHasClosed()
    {
        SkipWithoutBerlin();

        Resolved(Standard().NextWindow(Instant(20, 0, 0))).Date.ShouldBe(new DateOnly(2026, 8, 10));
    }

    // The difference from NextWindow: a caller asking when voting next starts
    // does not want to be told about the cycle that is already running.
    [Fact]
    public void NextOpeningAfterSkipsAWindowThatIsAlreadyOpen()
    {
        SkipWithoutBerlin();

        CycleWindow window = Resolved(Standard().NextOpeningAfter(Instant(12, 0, 0)));

        window.Date.ShouldBe(new DateOnly(2026, 8, 10));
        window.OpensAt.ShouldBeGreaterThan(Instant(12, 0, 0));
    }

    [Fact]
    public void NextOpeningAfterReportsTodaysWindowWhileItIsStillPending()
    {
        SkipWithoutBerlin();

        Resolved(Standard().NextOpeningAfter(Instant(6, 0, 0))).Date.ShouldBe(new DateOnly(2026, 8, 8));
    }

    // Pausing a guild is expressed by clearing the days, so every lookup has to
    // answer "never" rather than searching forever.
    [Fact]
    public void AScheduleWithNoDaysYieldsNoWindowsAtAll()
    {
        SkipWithoutBerlin();

        var paused = new CycleSchedule(CycleDays.None);

        paused.WindowFor(new DateOnly(2026, 8, 8)).ShouldBeNull();
        paused.CurrentWindow(Instant(12, 0, 0)).ShouldBeNull();
        paused.NextWindow(Instant(12, 0, 0)).ShouldBeNull();
        paused.NextOpeningAfter(Instant(12, 0, 0)).ShouldBeNull();
    }

    [Fact]
    public void AClosingTimeEarlierThanTheOpeningTimeIsReadAsAnOvernightWindow()
    {
        SkipWithoutBerlin();

        var schedule = new CycleSchedule(CycleDays.Daily, new TimeOnly(22, 0), new TimeOnly(6, 0));

        schedule.IsOvernight.ShouldBeTrue();

        CycleWindow window = Resolved(schedule.WindowFor(new DateOnly(2026, 8, 8)));

        window.Date.ShouldBe(new DateOnly(2026, 8, 8));
        window.OpensAt.ShouldBe(new DateTimeOffset(2026, 8, 8, 20, 0, 0, TimeSpan.Zero));
        window.ClosesAt.ShouldBe(new DateTimeOffset(2026, 8, 9, 4, 0, 0, TimeSpan.Zero));
        window.Duration.ShouldBe(TimeSpan.FromHours(8));
    }

    [Fact]
    public void AnOvernightWindowIsStillCurrentAfterMidnight()
    {
        SkipWithoutBerlin();

        var schedule = new CycleSchedule(CycleDays.Daily, new TimeOnly(22, 0), new TimeOnly(6, 0));

        CycleWindow window = Resolved(schedule.CurrentWindow(new DateTimeOffset(2026, 8, 9, 1, 0, 0, TimeSpan.Zero)));

        window.Date.ShouldBe(new DateOnly(2026, 8, 8));
    }

    [Fact]
    public void AnUnresolvableTimeZoneIsReportedRatherThanThrownByTryResolve()
    {
        var schedule = new CycleSchedule(timeZoneId: "Mars/Olympus_Mons");

        schedule.TryResolveTimeZone(out TimeZoneInfo? zone).ShouldBeFalse();
        zone.ShouldBeNull();
    }

    [Fact]
    public void AnUnresolvableTimeZoneThrowsFromResolve()
    {
        var schedule = new CycleSchedule(timeZoneId: "Mars/Olympus_Mons");

        _ = Should.Throw<TimeZoneNotFoundException>(schedule.ResolveTimeZone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructionRejectsAnEmptyTimeZoneIdentifier(string timeZoneId) => _ = Should.Throw<ArgumentException>(() => new CycleSchedule(timeZoneId: timeZoneId));

    [Fact]
    public void ConstructionRejectsANullTimeZoneIdentifier() => _ = Should.Throw<ArgumentNullException>(() => new CycleSchedule(timeZoneId: null!));

    // The documented cadence, constructed here rather than read from Default so
    // that these tests exercise the scheduling rules themselves and fail for one
    // reason only.
    private static CycleSchedule Standard() => new(CycleDays.Standard);

    private static DateTimeOffset Instant(int hour, int minute, int second)
        => new(2026, 8, 8, hour, minute, second, TimeSpan.Zero);

    private static CycleWindow Resolved(CycleWindow? window)
    {
        window.ShouldNotBeNull();

        return window.Value;
    }

    // Resolving "Europe/Berlin" needs the full ICU data set. The repository keeps
    // InvariantGlobalization false for exactly that reason — with it on, every
    // lookup throws and the scheduling code cannot work at all — but a stripped
    // container can still lack the data, and that is an environment gap rather
    // than a defect in the schedule.
    private static void SkipWithoutBerlin()
        => Assert.SkipUnless(
            TimeZoneInfo.TryFindSystemTimeZoneById(BERLIN, out _),
            "Europe/Berlin is unknown to this machine, so there is no ICU time zone data to test against.");
}
