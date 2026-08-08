using Fushi.Application.Abstractions.Discord;
using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence;
using Fushi.Application.Errors;
using Fushi.Application.Logging;
using Fushi.Core.Entities.Audits;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Results;

using FluentValidation;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Features.Cycles;

/// <summary>
/// Stops a guild's open cycle from accepting further votes, without deciding
/// anything.
/// </summary>
/// <remarks>
/// Closing and finalising are separate commands on purpose. Closing has to be
/// immediate and cheap: it is a single status change on one row, so the moment the
/// deadline passes there is no window in which a late vote can still be accepted.
/// Finalising is the opposite — it reads every submission's votes, applies the
/// policy, posts results, archives, and sends a direct message per applicant, any
/// of which can be slow or fail outright.
/// <br/>
/// Coupling them would mean a Discord outage during the results post left voting
/// open, which is precisely the failure that must not be possible: an applicant
/// could gather votes after the deadline while the bot retried. Splitting the two
/// makes the cheap guarantee unconditional and lets the expensive half be retried
/// as often as it needs to be.
/// </remarks>
/// <param name="GuildId">The guild whose cycle should stop taking votes.</param>
/// <param name="ActorId">
/// The user issuing the command, or <c>0</c> when the scheduler closes the cycle
/// because its window has elapsed.
/// </param>
/// <seealso cref="FinaliseCycle"/>
public sealed record CloseCycle(ulong GuildId, ulong ActorId) : ICommand;

/// <summary>
/// Checks the shape of a <see cref="CloseCycle"/> command.
/// </summary>
internal sealed class CloseCycleValidator : AbstractValidator<CloseCycle>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public CloseCycleValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");
    }
}

/// <summary>
/// Carries out <see cref="CloseCycle"/>.
/// </summary>
/// <param name="cycles">The cycle store.</param>
/// <param name="members">
/// Used to confirm a human caller may close the cycle. Not consulted when the
/// scheduler acts, because there is no member whose authority could be checked.
/// </param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">Supplies the current instant.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class CloseCycleHandler(
    ICycleRepository cycles,
    IGuildMemberLookup members,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<CloseCycleHandler> logger)
    : ICommandHandler<CloseCycle>
{
    /// <inheritdoc/>
    public async Task<Result> HandleAsync(
        CloseCycle request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ActorId != 0uL)
        {
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
        }

        Cycle? cycle = await cycles.FindOpenAsync(request.GuildId, cancellationToken);
        if (cycle is null)
        {
            return CycleErrors.NoneOpen;
        }

        DateTimeOffset now = clock.GetUtcNow();
        cycle.TransitionTo(CycleStatus.Closed, now, request.ActorId);

        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Cycle,
            AuditAction.CycleClosed,
            now,
            request.ActorId,
            cycle.Id,
            cycle.Code));

        CycleLog.Closed(logger, request.GuildId, cycle.Code, request.ActorId);

        return Result.Success();
    }
}
