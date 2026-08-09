using System.Globalization;
using System.Text;

using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Submissions;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// Renders domain values as the text a reader sees in a message.
/// </summary>
/// <remarks>
/// Enum names are not user-facing prose. <c>UnderReview</c> is a C# identifier;
/// "under review" is what belongs in an embed. Keeping the translation here
/// rather than at each call site is what stops the same status appearing three
/// different ways in three different commands.
/// </remarks>
internal static class Display
{
    private const int BAR_WIDTH = 12;

    /// <summary>
    /// Describes where a submission has reached.
    /// </summary>
    /// <param name="status">The submission's status.</param>
    /// <returns>The label to show.</returns>
    public static string Of(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Draft => "Draft",
        SubmissionStatus.Queued => "Queued",
        SubmissionStatus.UnderReview => "Under review",
        SubmissionStatus.Decided => "Decided",
        SubmissionStatus.Withdrawn => "Withdrawn",
        _ => status.ToString(),
    };

    /// <summary>
    /// Describes a submission's decision.
    /// </summary>
    /// <param name="outcome">
    /// The decision, or <see langword="null"/> when none has been reached.
    /// </param>
    /// <returns>The label to show.</returns>
    public static string Of(SubmissionOutcome? outcome) => outcome switch
    {
        SubmissionOutcome.Approved => "Approved",
        SubmissionOutcome.Rejected => "Rejected",

        // "Skipped" rather than "rejected" because the panel never reached a
        // verdict: too few people voted for the result to mean anything, and
        // saying otherwise would attribute a decision nobody made.
        SubmissionOutcome.Skipped => "Skipped (quorum not met)",
        _ => "Undecided",
    };

    /// <summary>
    /// Describes where a cycle has reached.
    /// </summary>
    /// <param name="status">The cycle's status.</param>
    /// <returns>The label to show.</returns>
    public static string Of(CycleStatus status) => status switch
    {
        CycleStatus.Scheduled => "Scheduled",
        CycleStatus.Open => "Open",
        CycleStatus.Closed => "Closed",
        CycleStatus.Finalised => "Finalised",
        CycleStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    /// <summary>
    /// Describes a vote.
    /// </summary>
    /// <param name="choice">The choice cast.</param>
    /// <returns>The label to show.</returns>
    public static string Of(VoteChoice choice) => choice switch
    {
        VoteChoice.Approve => "Approve",
        VoteChoice.Reject => "Reject",
        VoteChoice.Abstain => "Abstain",
        _ => choice.ToString(),
    };

    /// <summary>
    /// Names the days a cycle opens on.
    /// </summary>
    /// <remarks>
    /// The four preset combinations are named rather than enumerated, because
    /// "Weekdays" reads better than five day names and is what the person who
    /// configured it chose from.
    /// </remarks>
    /// <param name="days">The configured days.</param>
    /// <returns>The label to show.</returns>
    public static string Of(CycleDays days)
    {
        // An if-chain rather than a switch because CycleDays is a flag set, and
        // the interesting cases are the named combinations rather than the
        // individual members.
        if (days == CycleDays.None)
        {
            return "Paused — no days selected";
        }

        if (days == CycleDays.Standard)
        {
            return "Monday, Wednesday, Saturday";
        }

        if (days == CycleDays.Weekdays)
        {
            return "Weekdays";
        }

        if (days == CycleDays.Weekend)
        {
            return "Weekend";
        }

        return days == CycleDays.Daily ? "Every day" : NameDays(days);
    }

    /// <summary>
    /// Summarises the votes cast on a submission.
    /// </summary>
    /// <remarks>
    /// Each count is labelled rather than left to a symbol. A tally is read at a
    /// glance and then quoted in an argument about whether something passed, so
    /// which number is which has to survive being read out.
    /// </remarks>
    /// <param name="tally">The counts to render.</param>
    /// <returns>The label to show.</returns>
    public static string Of(VoteTally tally) => string.Create(
        CultureInfo.InvariantCulture,
        $"{tally.Approvals} approve · {tally.Rejections} reject · {tally.Abstentions} abstain");

    /// <summary>
    /// Draws the share of deciding votes that approved, against the share needed.
    /// </summary>
    /// <remarks>
    /// A bar rather than a number alone because the question a reader actually
    /// has is "is this passing", and a filled proportion answers it at a glance
    /// where <c>67%</c> next to <c>60%</c> takes a moment's arithmetic.
    /// Abstentions are absent from the bar because they are absent from the
    /// arithmetic.
    /// </remarks>
    /// <param name="tally">The votes cast.</param>
    /// <param name="requiredPercentage">The share needed to approve.</param>
    /// <returns>The bar, followed by the two percentages.</returns>
    public static string Bar(VoteTally tally, int requiredPercentage)
    {
        int filled = tally.DecidingVotes == 0
            ? 0
            : (int)Math.Round(tally.ApprovalPercentage / 100d * BAR_WIDTH, MidpointRounding.AwayFromZero);

        StringBuilder bar = new(BAR_WIDTH + 32);
        bar.Append('`');
        bar.Append('█', filled);
        bar.Append('░', BAR_WIDTH - filled);
        bar.Append('`');

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{bar} {tally.ApprovalPercentage}% of {requiredPercentage}% needed");
    }

    /// <summary>
    /// Describes a span of time in whole units.
    /// </summary>
    /// <remarks>
    /// Rounded to minutes because these spans are hours long and a reader
    /// deciding whether there is time to vote does not benefit from seconds.
    /// </remarks>
    /// <param name="span">The span to describe.</param>
    /// <returns>The label to show.</returns>
    public static string Duration(TimeSpan span)
    {
        if (span <= TimeSpan.Zero)
        {
            return "none";
        }

        int hours = (int)span.TotalHours;
        int minutes = span.Minutes;

        if (hours == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{minutes}m");
        }

        return minutes == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}h")
            : string.Create(CultureInfo.InvariantCulture, $"{hours}h {minutes}m");
    }

    /// <summary>
    /// Describes the window a cycle accepts votes in.
    /// </summary>
    /// <param name="opensAt">When voting opens, as wall-clock time.</param>
    /// <param name="closesAt">When voting closes, as wall-clock time.</param>
    /// <param name="timeZoneId">The zone those times are read in.</param>
    /// <returns>The label to show.</returns>
    public static string Window(TimeOnly opensAt, TimeOnly closesAt, string timeZoneId)
    {
        string overnight = closesAt <= opensAt ? " (overnight)" : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{opensAt:HH\\:mm}–{closesAt:HH\\:mm} {timeZoneId}{overnight}");
    }

    /// <summary>
    /// Names a channel by mention, or says it is unset.
    /// </summary>
    /// <param name="channelId">The channel, or <see langword="null"/> when unset.</param>
    /// <returns>The label to show.</returns>
    public static string Channel(ulong? channelId) =>
        channelId is { } id ? Core.Utilities.MentionUtility.Channel(id) : "*not set*";

    private static string NameDays(CycleDays days)
    {
        List<string> names = [];

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>().OrderBy(Ordinal))
        {
            if (days.HasFlag(CycleSchedule.FlagFor(day)))
            {
                names.Add(day.ToString());
            }
        }

        return names.Count == 0 ? "Paused — no days selected" : string.Join(", ", names);
    }

    // DayOfWeek starts its week on Sunday. A schedule reads better starting on
    // Monday, which is also the order the day picker offers.
    private static int Ordinal(DayOfWeek day) => ((int)day + 6) % 7;
}
