using System.ComponentModel.DataAnnotations;

namespace Fushi.Host.Options;

/// <summary>
/// How often the background services wake up.
/// </summary>
/// <remarks>
/// Both intervals are periods between passes rather than the instants anything
/// happens at. The scheduler is convergent — each pass compares the clock against
/// the configured window and corrects whatever it finds — so these values set how
/// late a transition can be, not whether it happens. Shortening them buys
/// punctuality at the cost of queries against guilds that have nothing to do.
/// </remarks>
public sealed class SchedulerOptions
{
    /// <summary>
    /// The configuration section these settings are bound from.
    /// </summary>
    public const string SECTION = "Scheduler";

    /// <summary>
    /// Gets or sets the seconds between scheduler passes.
    /// </summary>
    /// <value>
    /// Thirty seconds by default. A voting window is measured in hours, so a cycle
    /// opening half a minute late is invisible, and the alternative — a timer set
    /// for the exact instant — does not survive a restart or a clock change.
    /// </value>
    [Range(5, 3600)]
    public int TickSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the seconds between intake sweeps.
    /// </summary>
    /// <value>
    /// Two minutes by default. Longer than the scheduler's pass because each sweep
    /// costs a Discord history request per guild, and an application waiting two
    /// minutes to be acknowledged costs nobody anything — it is not judged until
    /// the next cycle opens regardless.
    /// </value>
    [Range(10, 3600)]
    public int IntakeSeconds { get; set; } = 120;

    /// <summary>
    /// Gets or sets a value indicating whether intake sweeps run at all.
    /// </summary>
    /// <value>
    /// <see langword="true"/> by default. Turn it off for an instance that should
    /// serve commands without collecting anything, which is the only safe way to
    /// run a second instance against one database until sweeping takes a lock.
    /// </value>
    public bool IntakeEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets how many messages one intake sweep reads per guild.
    /// </summary>
    [Range(1, 100)]
    public int IntakeBatchSize { get; set; } = 50;

    /// <summary>
    /// Gets or sets the seconds between guild registration passes.
    /// </summary>
    /// <value>
    /// Five minutes by default, the longest of the three. A pass costs one keyed
    /// lookup per guild and finds nothing to do on every run after the first, so
    /// running it often buys almost nothing. The one case it does serve — the bot
    /// being added to a server while it is running — tolerates the delay, because
    /// the first thing anybody does in a new server is read a panel, and the panel
    /// is drawn from defaults whether or not the row exists yet.
    /// </value>
    [Range(10, 3600)]
    public int RegistrationSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets a value indicating whether guild registration passes run.
    /// </summary>
    /// <value>
    /// <see langword="true"/> by default. Turning it off leaves rows to be created
    /// by whichever configuration command runs first, which works but means a
    /// brand-new server writes nothing until somebody changes a setting.
    /// </value>
    public bool RegistrationEnabled { get; set; } = true;
}
