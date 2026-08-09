using Discord;
using Discord.Interactions;

using Fushi.Core.Entities.Audits;
using Fushi.Core.Entities.Submissions;

namespace Fushi.Interactions.Modals;

/// <summary>
/// Collects the reason a submission is being withdrawn.
/// </summary>
/// <remarks>
/// A modal rather than a command option because it doubles as the confirmation:
/// withdrawal is terminal, and a dialogue that has to be filled in and submitted
/// is a deliberate act in a way that pressing enter on a slash command is not.
/// </remarks>
public sealed class WithdrawModal : IModal
{
    /// <inheritdoc/>
    public string Title => "Withdraw application";

    /// <summary>
    /// Gets or sets why the submission is being withdrawn.
    /// </summary>
    [InputLabel("Reason")]
    [ModalTextInput(
        "reason",
        TextInputStyle.Paragraph,
        "Optional. Recorded on the audit entry.",
        maxLength: AuditEntry.MAX_REASON_LENGTH)]
    [RequiredInput(false)]
    public string? Reason { get; set; }
}

/// <summary>
/// Collects the reason a cycle is being cancelled.
/// </summary>
/// <remarks>
/// Required rather than optional, unlike a withdrawal's reason. Cancelling
/// discards votes that people cast, and the record of why should not be able to
/// be empty.
/// </remarks>
public sealed class CancelCycleModal : IModal
{
    /// <inheritdoc/>
    public string Title => "Cancel cycle";

    /// <summary>
    /// Gets or sets why the cycle is being cancelled.
    /// </summary>
    [InputLabel("Reason")]
    [ModalTextInput(
        "reason",
        TextInputStyle.Paragraph,
        "Why is this cycle being abandoned?",
        maxLength: AuditEntry.MAX_REASON_LENGTH)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Collects the reason a voting grant exists.
/// </summary>
/// <remarks>
/// The one place in configuration where something is still typed, because a note
/// is prose and there is no menu that could offer it. It is deliberately not part
/// of granting: the grant is the decision, and being asked to justify it before it
/// takes effect would make the common case slower for the sake of the rare one.
/// </remarks>
public sealed class GrantNoteModal : IModal
{
    /// <inheritdoc/>
    public string Title => "Why does this grant exist?";

    /// <summary>
    /// Gets or sets the reason to store with the grant.
    /// </summary>
    [InputLabel("Note")]
    [ModalTextInput(
        "note",
        TextInputStyle.Paragraph,
        "Shown beside the grant in /voter list.",
        maxLength: AuditEntry.MAX_REASON_LENGTH)]
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// Collects a justification to attach to a vote already recorded.
/// </summary>
public sealed class VoteCommentModal : IModal
{
    /// <inheritdoc/>
    public string Title => "Comment on your vote";

    /// <summary>
    /// Gets or sets the justification to store with the vote.
    /// </summary>
    [InputLabel("Comment")]
    [ModalTextInput(
        "comment",
        TextInputStyle.Paragraph,
        "Visible to staff reviewing the audit trail, not to the channel.",
        maxLength: Vote.MAX_COMMENT_LENGTH)]
    public string Comment { get; set; } = string.Empty;
}
