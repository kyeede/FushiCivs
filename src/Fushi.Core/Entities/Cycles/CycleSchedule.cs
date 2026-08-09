using System.Diagnostics.CodeAnalysis;

namespace Fushi.Core.Entities.Cycles;

/// <summary>
/// A recurring rule describing when a guild accepts votes.
/// </summary>
/// <remarks>
/// The rule is expressed in wall-clock terms because that is how the people
/// running the server think about it: "Monday, Wednesday and Saturday, ten in
/// the morning until ten at night". Turning that into absolute instants is this
/// type's entire job, and it is not as mechanical as it looks.
/// <br/>
/// The time zone is stored as an IANA identifier such as <c>Europe/Berlin</c>
/// rather than as a fixed offset, because an offset cannot express "the local
/// working day". A guild on <c>Europe/Berlin</c> is on UTC+1 in January and
/// UTC+2 in July; storing <c>+02:00</c> would silently shift every winter cycle
/// by an hour. Resolution therefore happens per date, never once and cached.
/// <br/>
/// Two edge cases are handled explicitly rather than left to throw. When the
/// clocks go forward, the configured local time may not exist at all, and
/// resolution moves forward to the first instant that does. When they go back,
/// it exists twice, and opening takes the earlier instant while closing takes
/// the later one, so a window is never shortened by a transition.
/// </remarks>
/// <seealso cref="CycleWindow"/>
public readonly record struct CycleSchedule
{
    /// <summary>
    /// The time zone used when a guild has not chosen one, matching the
    /// Central European schedule the bot was built around.
    /// </summary>
    public const string DEFAULT_TIME_ZONE_ID = "Europe/Berlin";

    /// <summary>
    /// Initialises a recurring schedule.
    /// </summary>
    /// <param name="days">The days cycles run on.</param>
    /// <param name="opensAt">The local time voting opens.</param>
    /// <param name="closesAt">
    /// The local time voting closes. A value at or before
    /// <paramref name="opensAt"/> is read as closing on the following day,
    /// which is how an overnight window is expressed.
    /// </param>
    /// <param name="timeZoneId">
    /// The IANA time zone identifier the times are expressed in, such as
    /// <c>Europe/Berlin</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="timeZoneId"/> is empty or consists only of white space.
    /// </exception>
    public CycleSchedule(
        CycleDays days = CycleDays.Standard,
        TimeOnly opensAt = default,
        TimeOnly closesAt = default,
        string timeZoneId = DEFAULT_TIME_ZONE_ID
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        Days = days;
        OpensAt = opensAt == default ? new TimeOnly(10, 0) : opensAt;
        ClosesAt = closesAt == default ? new TimeOnly(22, 0) : closesAt;
        TimeZoneId = timeZoneId;
    }

    /// <summary>
    /// Gets the days cycles run on.
    /// </summary>
    /// <remarks>
    /// Unlike the three properties below, this one does not rewrite its zero value
    /// into a default. <see cref="CycleDays.None"/> is a meaningful setting — it is
    /// how a guild pauses without losing the rest of its configuration — so a
    /// getter that turned it back into <see cref="CycleDays.Standard"/> would make
    /// pausing impossible. The consequence is that <c>default(CycleSchedule)</c>
    /// never opens a cycle, which is why <see cref="Default"/> is written the way
    /// it is.
    /// </remarks>
    public CycleDays Days { get; init; }

    /// <summary>
    /// Gets the local time voting opens.
    /// </summary>
    public TimeOnly OpensAt => field == default ? new TimeOnly(10, 0) : field;

    /// <summary>
    /// Gets the local time voting closes.
    /// </summary>
    public TimeOnly ClosesAt => field == default ? new TimeOnly(22, 0) : field;

    /// <summary>
    /// Gets the IANA identifier of the time zone the times are expressed in.
    /// </summary>
    public string TimeZoneId => field ?? DEFAULT_TIME_ZONE_ID;

    /// <summary>
    /// Gets a value indicating whether the window runs past midnight into the
    /// following day.
    /// </summary>
    public bool IsOvernight => ClosesAt <= OpensAt;

    /// <summary>
    /// Gets the schedule used when a guild has not configured one: Monday,
    /// Wednesday, and Saturday, 10:00 to 22:00 Central European time.
    /// </summary>
    /// <remarks>
    /// The argument is not redundant, and removing it is a silent bug. A struct
    /// constructor whose parameters are all optional is still not a parameterless
    /// constructor, so <c>new()</c> binds to the implicit zero-initialising one and
    /// skips the defaults declared above it. That yields
    /// <see cref="CycleDays.None"/>, and a guild created from it would accept
    /// submissions and never once open a cycle to vote on them. Passing any
    /// argument forces the declared constructor to be chosen.
    /// </remarks>
    public static CycleSchedule Default => new(CycleDays.Standard);

    /// <summary>
    /// Resolves the configured time zone.
    /// </summary>
    /// <returns>The time zone the schedule is expressed in.</returns>
    /// <exception cref="TimeZoneNotFoundException">
    /// The identifier does not name a zone known to this machine. On a system
    /// built with invariant globalization every lookup fails, which is why
    /// <c>InvariantGlobalization</c> is disabled for this repository.
    /// </exception>
    public TimeZoneInfo ResolveTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    /// <summary>
    /// Attempts to resolve the configured time zone without throwing.
    /// </summary>
    /// <param name="timeZone">
    /// When this method returns <see langword="true"/>, the resolved zone;
    /// otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the identifier names a known zone.
    /// </returns>
    public bool TryResolveTimeZone([NotNullWhen(true)] out TimeZoneInfo? timeZone) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(TimeZoneId, out timeZone);

    /// <summary>
    /// Determines whether a local date is one the schedule runs on.
    /// </summary>
    /// <param name="date">The local date to test.</param>
    /// <returns>
    /// <see langword="true"/> when a cycle opens on that date.
    /// </returns>
    public bool IsCycleDay(DateOnly date) => Includes(date.DayOfWeek);

    /// <summary>
    /// Determines whether the schedule includes a given day of the week.
    /// </summary>
    /// <param name="day">The day to test.</param>
    /// <returns>
    /// <see langword="true"/> when cycles run on that day.
    /// </returns>
    public bool Includes(DayOfWeek day) => (Days & FlagFor(day)) != CycleDays.None;

    /// <summary>
    /// Resolves the schedule onto a specific local date.
    /// </summary>
    /// <param name="date">The local date to resolve.</param>
    /// <returns>
    /// The window for that date, or <see langword="null"/> when the schedule
    /// does not run on it.
    /// </returns>
    /// <exception cref="TimeZoneNotFoundException">
    /// The configured time zone is not known to this machine.
    /// </exception>
    public CycleWindow? WindowFor(DateOnly date)
    {
        if (!IsCycleDay(date))
        {
            return null;
        }

        TimeZoneInfo zone = ResolveTimeZone();
        DateOnly closingDate = IsOvernight ? date.AddDays(1) : date;

        DateTimeOffset opens = Resolve(date, OpensAt, zone, preferEarliest: true);
        DateTimeOffset closes = Resolve(closingDate, ClosesAt, zone, preferEarliest: false);

        return new CycleWindow(date, opens, closes);
    }

    /// <summary>
    /// Finds the window that is open at a given instant.
    /// </summary>
    /// <remarks>
    /// Checks the previous local date as well as the current one, because an
    /// overnight window that opened yesterday can still be open now.
    /// </remarks>
    /// <param name="instant">The instant to test.</param>
    /// <returns>
    /// The open window, or <see langword="null"/> when voting is not currently
    /// accepted.
    /// </returns>
    /// <exception cref="TimeZoneNotFoundException">
    /// The configured time zone is not known to this machine.
    /// </exception>
    public CycleWindow? CurrentWindow(DateTimeOffset instant)
    {
        TimeZoneInfo zone = ResolveTimeZone();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

        foreach (DateOnly date in (ReadOnlySpan<DateOnly>)[today.AddDays(-1), today])
        {
            if (WindowFor(date) is { } window && window.Contains(instant))
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the earliest window that has not finished as of a given instant.
    /// </summary>
    /// <remarks>
    /// Returns the currently open window when there is one, so a caller asking
    /// "what is next" during a cycle is told about the cycle in progress rather
    /// than being skipped past it. Use <see cref="NextOpeningAfter"/> when the
    /// intent is specifically the next time voting starts.
    /// </remarks>
    /// <param name="instant">The instant to search forward from.</param>
    /// <returns>
    /// The next window, or <see langword="null"/> when
    /// <see cref="Days"/> is <see cref="CycleDays.None"/>.
    /// </returns>
    /// <exception cref="TimeZoneNotFoundException">
    /// The configured time zone is not known to this machine.
    /// </exception>
    public CycleWindow? NextWindow(DateTimeOffset instant) => Search(instant, window => !window.HasClosed(instant));

    /// <summary>
    /// Finds the next window that has not yet opened as of a given instant.
    /// </summary>
    /// <param name="instant">The instant to search forward from.</param>
    /// <returns>
    /// The next window to open, or <see langword="null"/> when
    /// <see cref="Days"/> is <see cref="CycleDays.None"/>.
    /// </returns>
    /// <exception cref="TimeZoneNotFoundException">
    /// The configured time zone is not known to this machine.
    /// </exception>
    public CycleWindow? NextOpeningAfter(DateTimeOffset instant) => Search(instant, window => window.OpensAt > instant);

    private CycleWindow? Search(DateTimeOffset instant, Func<CycleWindow, bool> predicate)
    {
        if (Days == CycleDays.None)
        {
            return null;
        }

        TimeZoneInfo zone = ResolveTimeZone();
        DateOnly cursor = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime).AddDays(-1);

        // A schedule with at least one day set repeats within a week; the extra
        // days cover the overnight case and the day the search started behind.
        for (int offset = 0; offset <= 9; offset++)
        {
            if (WindowFor(cursor.AddDays(offset)) is { } window && predicate(window))
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>
    /// Converts a day of the week to its bit in <see cref="CycleDays"/>.
    /// </summary>
    /// <param name="day">The day to convert.</param>
    /// <returns>The corresponding single-day flag.</returns>
    public static CycleDays FlagFor(DayOfWeek day) =>
        day switch
        {
            DayOfWeek.Monday => CycleDays.Monday,
            DayOfWeek.Tuesday => CycleDays.Tuesday,
            DayOfWeek.Wednesday => CycleDays.Wednesday,
            DayOfWeek.Thursday => CycleDays.Thursday,
            DayOfWeek.Friday => CycleDays.Friday,
            DayOfWeek.Saturday => CycleDays.Saturday,
            DayOfWeek.Sunday => CycleDays.Sunday,
            _ => CycleDays.None,
        };

    private static DateTimeOffset Resolve(DateOnly date, TimeOnly time, TimeZoneInfo zone, bool preferEarliest)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        // Clocks went forward and this local time never happened. Step to the
        // first one that did, rather than throwing at a user who configured a
        // perfectly reasonable 02:30 start.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }

        if (zone.IsAmbiguousTime(local))
        {
            // Clocks went back and this local time happened twice. The larger
            // offset is the earlier instant, so opening takes it and closing
            // takes the other: the window is never shortened by the transition.
            TimeSpan[] offsets = zone.GetAmbiguousTimeOffsets(local);
            TimeSpan chosen = preferEarliest ? offsets.Max() : offsets.Min();

            return new DateTimeOffset(local, chosen).ToUniversalTime();
        }

        // UTC rather than the zone's wall-clock offset. Absolute instants are what
        // the scheduler compares against TimeProvider.GetUtcNow, and what Npgsql
        // will accept for timestamptz — a non-zero offset is rejected on write.
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Days} {OpensAt:HH\\:mm}-{ClosesAt:HH\\:mm} {TimeZoneId}";
}
