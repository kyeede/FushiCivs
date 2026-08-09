using System.Globalization;
using System.Text;

using Discord;

using Fushi.Application.Features.Submissions;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Utilities;
using Fushi.Core.Utilities.Paging;
using Fushi.Interactions.Components;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// Renders submissions.
/// </summary>
/// <remarks>
/// The review view carries its own voting buttons rather than having them
/// attached alongside. Under components v2 they live inside the same container as
/// the text they act on, which is both tidier and the only arrangement available:
/// a v2 message has no separate component tray to hang them from.
/// </remarks>
internal static class SubmissionViews
{
    private const int LIST_EXCERPT_LENGTH = 56;

    /// <summary>
    /// Renders one submission in full, as <c>/submission view</c> shows it.
    /// </summary>
    /// <param name="model">The submission to render.</param>
    /// <param name="actions">The buttons to offer, if any.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Detail(
        SubmissionDetailModel model,
        ActionRowBuilder? actions = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<IMessageComponentBuilder> parts =
        [
            Layout.Heading(model.Title),
            Body(model.Content, model.SourceUrl),
            Layout.Rule(),
            Layout.Fields(
                ("Applicant", model.ApplicantMention),
                ("Status", Display.Of(model.Status)),
                ("Outcome", Display.Of(model.Outcome))),
            Layout.Gap(),
            Layout.Subheading("Voting"),
            Layout.Text(Display.Bar(model.Tally, model.RequiredApprovalPercentage)),
            Layout.Fields(Voting(model)),
        ];

        parts.Add(Layout.Rule());
        parts.Add(Layout.Note(Provenance(model)));

        if (actions is not null)
        {
            parts.Add(actions);
        }

        return Layout.Panel(Palette.For(model.Outcome), [.. parts]);
    }

    /// <summary>
    /// Renders the review-channel message a panel votes on, buttons included.
    /// </summary>
    /// <remarks>
    /// Built from the entity rather than a read model because it is posted by the
    /// publisher during a command, at a point where the submission is already in
    /// memory and no query has been run.
    /// <br/>
    /// The buttons stay on a decided submission but are disabled, so the message
    /// still reads as a thing that was voted on rather than losing its shape once
    /// the cycle ends. Abstain is absent entirely when the guild forbids it: a
    /// control that exists but always refuses is worse than no control, because it
    /// invites the press and teaches nothing about why.
    /// </remarks>
    /// <param name="submission">The submission under review.</param>
    /// <param name="policy">The policy the cycle opened under.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Review(Submission submission, VotingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(submission);

        VoteTally tally = submission.Tally;
        bool decided = submission.Status != SubmissionStatus.UnderReview;
        string code = submission.Code.ToString();

        List<ButtonBuilder> buttons =
        [
            new(
                "Approve",
                ComponentIds.Vote(VoteChoice.Approve, code),
                ButtonStyle.Success,
                isDisabled: decided),
            new(
                "Reject",
                ComponentIds.Vote(VoteChoice.Reject, code),
                ButtonStyle.Danger,
                isDisabled: decided),
        ];

        if (policy.AllowAbstain)
        {
            buttons.Add(new ButtonBuilder(
                "Abstain",
                ComponentIds.Vote(VoteChoice.Abstain, code),
                ButtonStyle.Secondary,
                isDisabled: decided));
        }

        List<IMessageComponentBuilder> parts =
        [
            Layout.Heading(submission.Title),
            Body(submission.Content, SourceUrl(submission)),
            Layout.Rule(),
            Layout.Fields(
                ("Applicant", submission.Mention),
                ("Status", Display.Of(submission.Status)),
                ("Votes", Display.Of(tally))),
            Layout.Gap(),
            Layout.Text(Display.Bar(tally, policy.ApprovalPercentage)),
        ];

        if (submission.Outcome is { } outcome)
        {
            parts.Add(Layout.Text($"**Outcome** · {Display.Of(outcome)}"));
        }

        parts.Add(Layout.Rule());
        parts.Add(Layout.Actions([.. buttons]));
        parts.Add(Layout.Note($"Code {submission.Code} · {policy}"));

        return Layout.Panel(Palette.For(submission.Outcome), [.. parts]);
    }

    /// <summary>
    /// Renders the copy kept in the archive channel, and sent to the applicant.
    /// </summary>
    /// <param name="submission">The decided submission.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Archive(Submission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return Layout.Panel(
            Palette.For(submission.Outcome),
            Layout.Heading(submission.Title),
            Body(submission.Content, SourceUrl(submission)),
            Layout.Rule(),
            Layout.Fields(
                ("Applicant", submission.Mention),
                ("Outcome", Display.Of(submission.Outcome)),
                ("Votes", Display.Of(submission.Tally))),
            Layout.Note($"Code {submission.Code}"));
    }

    /// <summary>
    /// Renders a page of submissions, each row able to open itself.
    /// </summary>
    /// <remarks>
    /// Ten rows, each a section carrying a button, is thirty of the forty
    /// components a message may hold — which is why the navigation is a single row
    /// and nothing is separated row from row.
    /// </remarks>
    /// <param name="page">The page to render.</param>
    /// <param name="heading">The list's title.</param>
    /// <param name="navigation">The paging buttons.</param>
    /// <returns>The message.</returns>
    public static MessageComponent List(
        Page<SubmissionSummaryModel> page,
        string heading,
        ActionRowBuilder navigation)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.Info.IsEmpty)
        {
            return Layout.Panel(
                Palette.Muted,
                Layout.Heading(heading),
                Layout.Text("Nothing here yet."),
                navigation);
        }

        List<IMessageComponentBuilder> parts = [Layout.Heading(heading), Layout.Rule()];

        foreach (SubmissionSummaryModel item in page)
        {
            StringBuilder line = new(128);

            line.Append(CultureInfo.InvariantCulture, $"`{item.Code}` **{Excerpt(item.Title)}**\n");
            line.Append(
                CultureInfo.InvariantCulture,
                $"-# {item.ApplicantMention} · {Display.Of(item.Status)}");

            if (item.Outcome is { } outcome)
            {
                line.Append(CultureInfo.InvariantCulture, $" · {Display.Of(outcome)}");
            }

            parts.Add(Layout.Row(line.ToString(), "View", ComponentIds.Open(item.Code)));
        }

        parts.Add(Layout.Note(Pager.Position(page.Info)));
        parts.Add(navigation);

        return Layout.Panel(Palette.Neutral, [.. parts]);
    }

    /// <summary>
    /// Renders a submission's text beside a link to where it came from.
    /// </summary>
    /// <remarks>
    /// A section with a link accessory rather than a title hyperlink, which is
    /// what an embed used. The original message is worth one press when a
    /// moderator wants the attachments or the surrounding conversation, and a
    /// button says that far more clearly than a title that happens to be blue.
    /// </remarks>
    /// <param name="content">The submission's text.</param>
    /// <param name="sourceUrl">Where it was posted, if that is known.</param>
    /// <returns>The component.</returns>
    private static IMessageComponentBuilder Body(string content, string? sourceUrl)
    {
        string text = string.IsNullOrWhiteSpace(content)
            ? "*No text was posted with this application.*"
            : Layout.Clamp(content);

        // A section must carry an accessory, and a link button must carry a URL,
        // so a submission with no recorded source degrades to plain text rather
        // than to a button Discord would refuse to build.
        return string.IsNullOrWhiteSpace(sourceUrl)
            ? Layout.Text(text)
            : new SectionBuilder()
                .AddComponent(Layout.Text(text))
                .WithAccessory(new ButtonBuilder(
                    label: "Original",
                    customId: null,
                    style: ButtonStyle.Link,
                    url: sourceUrl));
    }

    private static (string, string)[] Voting(SubmissionDetailModel model)
    {
        List<(string, string)> fields = [("Tally", Display.Of(model.Tally))];

        if (model.RequiredQuorum > 0)
        {
            int shortfall = model.RequiredQuorum - model.Tally.DecidingVotes;

            fields.Add((
                "Quorum",
                shortfall > 0
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"{model.Tally.DecidingVotes} of {model.RequiredQuorum} — {shortfall} more needed")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"{model.Tally.DecidingVotes} of {model.RequiredQuorum} — met")));
        }

        if (model.CycleCode is { } cycle)
        {
            fields.Add(("Cycle", $"`{cycle}`"));
        }

        return [.. fields];
    }

    private static string Provenance(SubmissionDetailModel model)
    {
        StringBuilder note = new(96);

        note.Append(CultureInfo.InvariantCulture, $"Code {model.Code} · captured ");
        note.Append(MentionUtility.Timestamp(model.CapturedAt, TimestampStyle.Relative));

        if (model.DecidedAt is { } decidedAt)
        {
            note.Append(" · decided ");
            note.Append(MentionUtility.Timestamp(decidedAt, TimestampStyle.Relative));
        }

        return note.ToString();
    }

    private static string SourceUrl(Submission submission) => string.Create(
        CultureInfo.InvariantCulture,
        $"https://discord.com/channels/{submission.GuildId}/{submission.SourceChannelId}/{submission.SourceMessageId}");

    private static string Excerpt(string title) => title.Length <= LIST_EXCERPT_LENGTH
        ? title
        : string.Concat(title.AsSpan(0, LIST_EXCERPT_LENGTH - 1).TrimEnd(), "…");
}
