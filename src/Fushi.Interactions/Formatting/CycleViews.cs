using System.Globalization;
using System.Text;

using Discord;

using Fushi.Application.Features.Cycles;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Utilities;
using Fushi.Core.Utilities.Paging;
using Fushi.Interactions.Components;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// Renders voting cycles.
/// </summary>
internal static class CycleViews
{
    private const int TITLE_EXCERPT_LENGTH = 44;

    /// <summary>
    /// Renders the state of the current cycle, as <c>/cycle status</c> shows it.
    /// </summary>
    /// <param name="model">The current cycle, and when the next one opens.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Status(CycleStatusModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.Current is not { } current)
        {
            return model.NextOpensAt is { } next
                ? Layout.Panel(
                    Palette.Muted,
                    Layout.Heading("No cycle is open"),
                    Layout.Text(
                        $"The next one opens {MentionUtility.Timestamp(next, TimestampStyle.Relative)} "
                        + $"({MentionUtility.Timestamp(next, TimestampStyle.ShortDateTime)})."))
                : Layout.Panel(
                    Palette.Caution,
                    Layout.Heading("No cycle is scheduled"),
                    Layout.Text(
                        "Check `/config show` — the guild may be disabled, unconfigured, or paused "
                        + "with no cycle days selected."));
        }

        return Layout.Panel(
            Palette.Success,
            Layout.Heading($"Cycle {current.Code} is open"),
            Layout.Text(
                $"Voting closes {MentionUtility.Timestamp(current.ClosesAt, TimestampStyle.Relative)} "
                + $"({MentionUtility.Timestamp(current.ClosesAt, TimestampStyle.ShortTime)})."),
            Layout.Rule(),
            Layout.Fields(
                ("Opened", MentionUtility.Timestamp(current.OpensAt, TimestampStyle.ShortDateTime)),
                ("Time left", Display.Duration(current.Remaining)),
                (
                    "Progress",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{current.VotedCount} of {current.SubmissionCount} have votes")),
                ("Policy", current.Policy.ToString())),
            Layout.Note($"Scheduled for {current.Date:yyyy-MM-dd}"));
    }

    /// <summary>
    /// Renders the public announcement posted when a cycle opens.
    /// </summary>
    /// <param name="cycle">The cycle that opened.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Announcement(Cycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        return Layout.Panel(
            Palette.Success,
            Layout.Heading($"Voting is open — cycle {cycle.Code}"),
            Layout.Text(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{cycle.Submissions.Count} submission(s) are up for review. Voting closes ")
                + MentionUtility.Timestamp(cycle.ClosesAt, TimestampStyle.Relative)
                + "."),
            Layout.Rule(),
            Layout.Fields(
                ("Opens", MentionUtility.Timestamp(cycle.OpensAt, TimestampStyle.ShortDateTime)),
                ("Closes", MentionUtility.Timestamp(cycle.ClosesAt, TimestampStyle.ShortDateTime)),
                ("Passing", cycle.Policy.ToString())),
            Layout.Note($"Scheduled for {cycle.ScheduledDate:yyyy-MM-dd}"));
    }

    /// <summary>
    /// Renders the public results posted when a cycle is finalised.
    /// </summary>
    /// <remarks>
    /// The per-submission lines are one text block rather than a section each,
    /// because a finalised cycle may hold far more submissions than the forty
    /// components a message is allowed. Anything past what fits is summarised on
    /// the last line instead of being silently dropped.
    /// </remarks>
    /// <param name="cycle">The finalised cycle, with its submissions loaded.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Results(Cycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        List<Submission> submissions = [.. cycle.Submissions.OrderBy(s => s.Code)];

        int approved = submissions.Count(s => s.Outcome == SubmissionOutcome.Approved);
        int rejected = submissions.Count(s => s.Outcome == SubmissionOutcome.Rejected);
        int skipped = submissions.Count(s => s.Outcome == SubmissionOutcome.Skipped);

        StringBuilder lines = new(submissions.Count * 80);
        int shown = 0;

        foreach (Submission submission in submissions)
        {
            if (lines.Length > Layout.MAX_BODY_LENGTH)
            {
                break;
            }

            lines.Append(CultureInfo.InvariantCulture, $"{Display.Of(submission.Outcome)} ");
            lines.Append(CultureInfo.InvariantCulture, $"`{submission.Code}` {Excerpt(submission.Title)}");
            lines.Append(CultureInfo.InvariantCulture, $" — {Display.Of(submission.Tally)}\n");
            shown++;
        }

        if (shown < submissions.Count)
        {
            lines.Append(
                CultureInfo.InvariantCulture,
                $"\n*…and {submissions.Count - shown} more.*");
        }

        return Layout.Panel(
            Palette.Neutral,
            Layout.Heading($"Results — cycle {cycle.Code}"),
            Layout.Fields(
                ("Approved", approved.ToString(CultureInfo.InvariantCulture)),
                ("Rejected", rejected.ToString(CultureInfo.InvariantCulture)),
                ("Skipped", skipped.ToString(CultureInfo.InvariantCulture))),
            Layout.Rule(),
            Layout.Text(lines.Length == 0 ? "No submissions were reviewed." : lines.ToString()),
            Layout.Note($"{cycle.Policy} · closed {cycle.ClosesAt:yyyy-MM-dd HH:mm}"));
    }

    /// <summary>
    /// Renders the private confirmation the person who finalised a cycle sees.
    /// </summary>
    /// <param name="model">The counts the finalisation produced.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Receipt(CycleResultsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return Layout.Panel(
            Palette.Success,
            Layout.Heading($"Cycle {model.Code} finalised"),
            Layout.Text(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{model.SubmissionCount} submission(s) decided.")),
            Layout.Rule(),
            Layout.Fields(
                ("Approved", model.Approved.ToString(CultureInfo.InvariantCulture)),
                ("Rejected", model.Rejected.ToString(CultureInfo.InvariantCulture)),
                ("Skipped", model.Skipped.ToString(CultureInfo.InvariantCulture))));
    }

    /// <summary>
    /// Renders a page of past cycles, each row offering whatever can still be done
    /// to it.
    /// </summary>
    /// <remarks>
    /// The action a cycle needs is a function of the state it is in — an open one
    /// can be closed, a closed one finalised, a scheduled one abandoned — so the
    /// row that shows the state is the right place to offer it. What that removes
    /// is the step where somebody read a short code off this list and typed it into
    /// <c>/cycle finalise</c>, which is the step a code can be mistyped in.
    /// <br/>
    /// A finished cycle gets no button and so is rendered as plain text rather than
    /// as a section, which both keeps the row honest and keeps a full page inside
    /// the forty components a message may carry.
    /// </remarks>
    /// <param name="page">The page to render.</param>
    /// <param name="navigation">The paging buttons.</param>
    /// <returns>The message.</returns>
    public static MessageComponent List(
        Page<CycleSummaryModel> page,
        ActionRowBuilder navigation)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.Info.IsEmpty)
        {
            return Layout.Panel(
                Palette.Muted,
                Layout.Heading("Cycles"),
                Layout.Text("No cycles have run yet."),
                navigation);
        }

        List<IMessageComponentBuilder> parts = [Layout.Heading("Cycles"), Layout.Rule()];

        foreach (CycleSummaryModel item in page)
        {
            StringBuilder line = new(120);

            line.Append(CultureInfo.InvariantCulture, $"`{item.Code}` **{item.Date:yyyy-MM-dd}** ");
            line.Append(CultureInfo.InvariantCulture, $"{Display.Of(item.Status)}\n");
            line.Append(
                CultureInfo.InvariantCulture,
                $"-# {item.SubmissionCount} submission(s) · {item.Approved} approved · {item.Rejected} rejected · {item.Skipped} skipped");

            parts.Add(Action(item) is { } action
                ? Layout.Row(line.ToString(), action.Label, action.CustomId, action.Style)
                : Layout.Text(line.ToString()));
        }

        parts.Add(Layout.Note(Pager.Position(page.Info)));
        parts.Add(navigation);

        return Layout.Panel(Palette.Neutral, [.. parts]);
    }

    /// <summary>
    /// Chooses the one thing a cycle in a given state most needs doing to it.
    /// </summary>
    /// <remarks>
    /// One button per row, because a section carries a single accessory. Closing
    /// beats cancelling for an open cycle and finalising beats cancelling for a
    /// closed one, on the grounds that the ordinary path through a cycle should be
    /// the one that is one press away and the destructive path should not be.
    /// </remarks>
    /// <param name="item">The cycle to judge.</param>
    /// <returns>The button to offer, or nothing when it is already decided.</returns>
    private static (string Label, string CustomId, ButtonStyle Style)? Action(CycleSummaryModel item)
        => item.Status switch
        {
            CycleStatus.Open => (
                "Close",
                ComponentIds.CycleAction("close"),
                ButtonStyle.Primary),
            CycleStatus.Closed => (
                "Finalise",
                ComponentIds.CycleAction("final", item.Code.ToString()),
                ButtonStyle.Success),
            CycleStatus.Scheduled => (
                "Cancel",
                ComponentIds.CycleAction("cancel", item.Code.ToString()),
                ButtonStyle.Danger),
            CycleStatus.Finalised or CycleStatus.Cancelled => null,
            _ => null,
        };

    private static string Excerpt(string title) => title.Length <= TITLE_EXCERPT_LENGTH
        ? title
        : string.Concat(title.AsSpan(0, TITLE_EXCERPT_LENGTH - 1).TrimEnd(), "…");
}
