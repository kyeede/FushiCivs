using Fushi.Application.Abstractions.Discord;
using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence;
using Fushi.Application.Errors;
using Fushi.Application.Logging;
using Fushi.Core.Entities.Audits;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;
using Fushi.Core.Results;

using FluentValidation;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Features.Cycles;

/// <summary>
/// Abandons a cycle before its outcomes are applied, returning its submissions to
/// the queue.
/// </summary>
/// <remarks>
/// The escape hatch for a cycle that should not have run: opened on the wrong day,
/// opened with the wrong policy, or opened while half the moderators were away. No
/// submission is judged, and every one of them waits for the next cycle instead.
/// <br/>
/// The votes already cast are cleared rather than carried forward, which
/// <see cref="Submission.ReturnToQueue"/> does as part of requeueing. They were
/// cast under terms that no longer count — possibly a different threshold, and
/// certainly a different set of people who happened to be around — and keeping
/// them would let one person's single decision apply to two separate votes. A
/// voter who still holds the same opinion can say so again in a minute; a voter
/// whose opinion has changed cannot take back a vote they were never told had been
/// reused.
/// <br/>
/// A reason is required rather than optional. Cancelling discards work people have
/// already done, and the audit trail is the only place they can find out why.
/// </remarks>
/// <param name="GuildId">The guild the cycle belongs to.</param>
/// <param name="Code">The cycle to abandon.</param>
/// <param name="ActorId">The user issuing the cancellation.</param>
/// <param name="Reason">Why the cycle is being abandoned.</param>
public sealed record CancelCycle(
    ulong GuildId,
    ShortCode Code,
    ulong ActorId,
    string Reason) : ICommand;

/// <summary>
/// Checks the shape of a <see cref="CancelCycle"/> command.
/// </summary>
/// <remarks>
/// The actor must be a real user here, unlike opening or closing: the scheduler
/// never cancels anything on its own, so a zero actor would mean the caller failed
/// to supply one rather than that the bot acted by itself.
/// </remarks>
internal sealed class CancelCycleValidator : AbstractValidator<CancelCycle>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public CancelCycleValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(command => command.ActorId)
            .NotEqual(0uL)
            .WithMessage("An acting user is required.");

        RuleFor(command => command.Code)
            .Must(code => !code.IsEmpty)
            .WithMessage("A cycle code is required.");

        RuleFor(command => command.Reason)
            .NotEmpty()
            .WithMessage("Give a reason for cancelling the cycle.")
            .MaximumLength(AuditEntry.MAX_REASON_LENGTH)
            .WithMessage($"A reason is at most {AuditEntry.MAX_REASON_LENGTH} characters.");
    }
}

/// <summary>
/// Carries out <see cref="CancelCycle"/>.
/// </summary>
/// <param name="cycles">The cycle store.</param>
/// <param name="members">Used to confirm the actor may administer the guild.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">Supplies the current instant.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class CancelCycleHandler(
    ICycleRepository cycles,
    IGuildMemberLookup members,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<CancelCycleHandler> logger)
    : ICommandHandler<CancelCycle>
{
    /// <inheritdoc/>
    public async Task<Result> HandleAsync(
        CancelCycle request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<bool> authority = await members.IsAdministratorAsync(
            request.GuildId,
            request.ActorId,
            cancellationToken);

        if (authority.IsFailure)
        {
            return authority.Error;
        }

        if (!authority.Value)
        {
            return GuildErrors.Forbidden;
        }

        Cycle? cycle = await cycles.FindByCodeAsync(
            request.GuildId,
            request.Code,
            cancellationToken);

        if (cycle is null)
        {
            return CycleErrors.NotFound(request.Code);
        }

        // Cycle.TransitionTo refuses to leave a terminal state by throwing, so
        // the state is checked here and reported as a failure the caller can read.
        if (cycle.IsTerminal)
        {
            return CycleErrors.Concluded(cycle.Code);
        }

        DateTimeOffset now = clock.GetUtcNow();
        CycleStatus abandonedFrom = cycle.Status;
        int requeued = 0;

        foreach (Submission submission in cycle.Submissions)
        {
            // A submission already decided or withdrawn is left alone. Requeueing
            // one would throw, and there is nothing to return: it left this cycle
            // before the cancellation reached it.
            if (submission.IsTerminal)
            {
                continue;
            }

            submission.ReturnToQueue(now, request.ActorId);
            requeued++;

            audit.Record(AuditEntry.Record(
                request.GuildId,
                AuditScope.Submission,
                AuditAction.SubmissionQueued,
                now,
                request.ActorId,
                submission.Id,
                submission.Code,
                reason: request.Reason));
        }

        cycle.TransitionTo(CycleStatus.Cancelled, now, request.ActorId);

        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Cycle,
            AuditAction.CycleCancelled,
            now,
            request.ActorId,
            cycle.Id,
            cycle.Code,
            reason: request.Reason,
            metadata: System.Text.Json.JsonSerializer.Serialize(new
            {
                StatusBefore = abandonedFrom.ToString(),
                RequeuedSubmissions = requeued,
            })));

        CycleLog.Cancelled(
            logger,
            request.GuildId,
            cycle.Code,
            request.ActorId,
            requeued,
            request.Reason);

        return Result.Success();
    }
}
