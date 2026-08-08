namespace Fushi.Core.Entities.Cycles;

/// <summary>
/// The days of the week on which a guild runs a voting cycle.
/// </summary>
/// <remarks>
/// A bit field rather than a collection so that the whole schedule fits in a
/// single small column and a membership test is one bitwise operation. The
/// combinations at the bottom exist because they are the ones people actually
/// ask for.
/// </remarks>
/// <seealso cref="CycleSchedule"/>
[Flags]
public enum CycleDays
{
    /// <summary>
    /// No days. A schedule with no days never opens a cycle, which is how a
    /// guild pauses without losing its configuration.
    /// </summary>
    None = 0,

    /// <summary>Monday.</summary>
    Monday = 1 << 0,

    /// <summary>Tuesday.</summary>
    Tuesday = 1 << 1,

    /// <summary>Wednesday.</summary>
    Wednesday = 1 << 2,

    /// <summary>Thursday.</summary>
    Thursday = 1 << 3,

    /// <summary>Friday.</summary>
    Friday = 1 << 4,

    /// <summary>Saturday.</summary>
    Saturday = 1 << 5,

    /// <summary>Sunday.</summary>
    Sunday = 1 << 6,

    /// <summary>
    /// Monday, Wednesday, and Saturday: the default cadence, spacing three
    /// cycles across a week without putting two on consecutive days.
    /// </summary>
    Standard = Monday | Wednesday | Saturday,

    /// <summary>Monday through Friday.</summary>
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,

    /// <summary>Saturday and Sunday.</summary>
    Weekend = Saturday | Sunday,

    /// <summary>Every day of the week.</summary>
    Daily = Weekdays | Weekend,
}
