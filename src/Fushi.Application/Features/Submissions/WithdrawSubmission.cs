using Fushi.Application.Abstractions.Discord;
using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Application.Errors;
using Fushi.Application.Logging;
using Fushi.Core.Entities.Audits;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;
using Fushi.Core.Results;

using FluentValidation;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Features.Submissions;

/// <summary>
/// Takes a submission out of consideration before it is judged.
/// </summary>
/// <remarks>
/// Withdrawal is terminal but not a verdict. A submission that its applicant
/// took back carries no <see cref="SubmissionOutcome"/> at all, which is what
/// keeps "changed their mind" from reading like "the server said no" when the
/// history is looked at later.
/// <br/>
/// Either the applicant or somebody who can manage the server may do it. There
/// is no separate moderator list to maintain: authority comes from Discord's own
/// permissions, so revoking it there revokes it here.
/// </remarks>
/// <param name="GuildId">The guild the submission belongs to.</param>
/// <param name="ActorId">The user asking for the withdrawal.</param>
/// <param name="Code">The submission's public code, as the user typed it.</param>
/// <param name="Reason">
/// Why it is being withdrawn, or <see langword="null"/> when none was given.
/// Recorded on the audit entry, which is the only place it is kept.
/// </param>
public sealed record WithdrawSubmission(
    ulong GuildId,
    ulong ActorId,
    string Code,
    string? Reason = null) : ICommand;

/// <summary>
/// Checks the shape of a <see cref="WithdrawSubmission"/> command.
/// </summary>
/// <remarks>
/// Whether the code resolves to anything, and whether the caller is entitled to
/// withdraw it, both need the database. Only the length of the reason can be
/// judged here, and it is worth judging: an over-long one would otherwise throw
/// out of <see cref="AuditEntry"/> once the change had already been made.
/// </remarks>
internal sealed class WithdrawSubmissionValidator : AbstractValidator<WithdrawSubmission>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public WithdrawSubmissionValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(command => command.ActorId)
            .NotEqual(0uL)
            .WithMessage("An acting user is required.");

        RuleFor(command => command.Code)
            .NotEmpty()
            .WithMessage("A submission code is required.");

        RuleFor(command => command.Reason)
            .MaximumLength(AuditEntry.MAX_REASON_LENGTH)
            .WithMessage($"A reason can be at most {AuditEntry.MAX_REASON_LENGTH} characters.");
    }
}

/// <summary>
/// Carries out <see cref="WithdrawSubmission"/>.
/// </summary>
/// <param name="submissions">The submission store.</param>
/// <param name="members">Used to confirm the actor may act for somebody else.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">
/// Supplies the current instant. <see cref="TimeProvider"/> rather than a bespoke
/// clock interface: it is the framework's own abstraction, and tests can
/// substitute a fake without this project defining one.
/// </param>
/// <param name="logger">The logger to write to.</param>
internal sealed class WithdrawSubmissionHandler(
    ISubmissionRepository submissions,
    IGuildMemberLookup members,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<WithdrawSubmissionHandler> logger)
    : ICommandHandler<WithdrawSubmission>
{
    /// <inheritdoc/>
    public async Task<Result> HandleAsync(
        WithdrawSubmission request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ShortCode.TryParse(request.Code, out ShortCode code))
        {
            return SubmissionErrors.MalformedCode(request.Code);
        }

        Submission? submission = await submissions.FindByCodeAsync(
            request.GuildId,
            code,
            cancellationToken);

        if (submission is null)
        {
            return SubmissionErrors.NotFound(code);
        }

        if (submission.ApplicantId != request.ActorId)
        {
            Result<bool> authority = await members.IsAdministratorAsync(
                request.GuildId,
                request.ActorId,
                cancellationToken);

            // A failed lookup is propagated rather than converted into a
            // refusal. "Discord did not answer" and "you are not allowed" call
            // for different things from the person reading the reply.
            if (authority.IsFailure)
            {
                return authority.Error;
            }

            if (!authority.Value)
            {
                return SubmissionErrors.NotYours;
            }
        }

        if (submission.Status is SubmissionStatus.Decided)
        {
            return submission.Outcome is { } outcome
                ? SubmissionErrors.AlreadyDecided(code, outcome)
                : SubmissionErrors.WrongState(code, submission.Status);
        }

        if (submission.Status is SubmissionStatus.Withdrawn)
        {
            return SubmissionErrors.WrongState(code, submission.Status);
        }

        DateTimeOffset now = clock.GetUtcNow();
        submission.Withdraw(now, request.ActorId);

        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Submission,
            AuditAction.SubmissionWithdrawn,
            now,
            request.ActorId,
            subjectId: submission.Id,
            subjectCode: code,
            targetId: submission.ApplicantId,
            reason: request.Reason));

        string rendered = code.ToString();
        SubmissionLog.Withdrawn(
            logger,
            request.GuildId,
            rendered,
            request.ActorId,
            request.Reason);

        return Result.Success();
    }
}
