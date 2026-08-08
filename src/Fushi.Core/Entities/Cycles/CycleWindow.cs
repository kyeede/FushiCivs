namespace Fushi.Core.Entities.Cycles;

/// <summary>
/// A concrete span of time during which votes are accepted, resolved from a
/// <see cref="CycleSchedule"/> onto a specific date.
/// </summary>
/// <remarks>
/// The schedule says "Saturdays, 10:00 to 22:00 Berlin time"; a window says
/// "2026-08-08T08:00:00Z to 2026-08-08T20:00:00Z". Everything downstream works
/// in absolute instants, so the ambiguity of a wall clock is dealt with exactly
/// once, at the point the window is produced.
/// </remarks>
/// <seealso cref="CycleSchedule.WindowFor"/>
public readonly record struct CycleWindow
{
    /// <summary>
    /// Initialises a window from its resolved boundaries.
    /// </summary>
    /// <param name="date">
    /// The local date the window belongs to, used to label the cycle.
    /// </param>
    /// <param name="opensAt">The instant voting opens.</param>
    /// <param name="closesAt">The instant voting closes.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="closesAt"/> is not after <paramref name="opensAt"/>.
    /// </exception>
    public CycleWindow(DateOnly date, DateTimeOffset opensAt, DateTimeOffset closesAt)
    {
        if (closesAt <= opensAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(closesAt),
                closesAt,
                "A voting window must close after it opens."
            );
        }

        Date = date;
        OpensAt = opensAt;
        ClosesAt = closesAt;
    }

    /// <summary>
    /// Gets the local date this window belongs to.
    /// </summary>
    /// <remarks>
    /// The date in the guild's own time zone, not in UTC. A window that opens
    /// at 10:00 Berlin time on a Saturday belongs to that Saturday even when
    /// the corresponding UTC instant falls on a different date.
    /// </remarks>
    public DateOnly Date { get; }

    /// <summary>
    /// Gets the instant voting opens.
    /// </summary>
    public DateTimeOffset OpensAt { get; }

    /// <summary>
    /// Gets the instant voting closes.
    /// </summary>
    public DateTimeOffset ClosesAt { get; }

    /// <summary>
    /// Gets how long the window stays open.
    /// </summary>
    /// <remarks>
    /// Not necessarily the difference between the configured wall-clock times:
    /// a window spanning a daylight saving transition is an hour longer or
    /// shorter than it looks on a clock.
    /// </remarks>
    public TimeSpan Duration => ClosesAt - OpensAt;

    /// <summary>
    /// Determines whether an instant falls inside the window.
    /// </summary>
    /// <remarks>
    /// The interval is half-open: the opening instant is inside, the closing
    /// instant is not. A vote cast at exactly the closing time is late, which
    /// is the only reading that keeps two adjacent windows from both claiming
    /// the same instant.
    /// </remarks>
    /// <param name="instant">The instant to test.</param>
    /// <returns>
    /// <see langword="true"/> when the window is open at that instant.
    /// </returns>
    public bool Contains(DateTimeOffset instant) => instant >= OpensAt && instant < ClosesAt;

    /// <summary>
    /// Determines whether the window has already opened as of an instant.
    /// </summary>
    /// <param name="instant">The instant to measure from.</param>
    /// <returns><see langword="true"/> when opening has passed.</returns>
    public bool HasOpened(DateTimeOffset instant) => instant >= OpensAt;

    /// <summary>
    /// Determines whether the window has already closed as of an instant.
    /// </summary>
    /// <param name="instant">The instant to measure from.</param>
    /// <returns><see langword="true"/> when closing has passed.</returns>
    public bool HasClosed(DateTimeOffset instant) => instant >= ClosesAt;

    /// <summary>
    /// Calculates how long remains before the window closes.
    /// </summary>
    /// <param name="instant">The instant to measure from.</param>
    /// <returns>
    /// The remaining time, or <see cref="TimeSpan.Zero"/> when the window has
    /// already closed.
    /// </returns>
    public TimeSpan RemainingFrom(DateTimeOffset instant)
    {
        TimeSpan remaining = ClosesAt - instant;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Date:yyyy-MM-dd}: {OpensAt:u} to {ClosesAt:u}";
}
