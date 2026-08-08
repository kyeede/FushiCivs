using Fushi.Application.Abstractions.Discord;
using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Application.Errors;
using Fushi.Application.Logging;
using Fushi.Core.Entities.Audits;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Results;

using FluentValidation;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Features.Guilds;

/// <summary>
/// Switches the bot on or off for a guild.
/// </summary>
/// <remarks>
/// Disabling stops new cycles from opening. It does not discard queued
/// submissions, and it does not close a cycle that is already taking votes: the
/// people who applied did nothing wrong, and the people voting were told a
/// closing time. A server that wants to stop mid-cycle cancels the cycle; this
/// command is the one it uses to go on a break.
/// <br/>
/// Nothing is deleted either way, so the switch is safe to flip back. That is
/// what makes it the right answer to "we are pausing recruitment for a month",
/// rather than clearing the configuration and setting it up again later.
/// </remarks>
/// <param name="GuildId">The guild being switched.</param>
/// <param name="ActorId">The user issuing the change.</param>
/// <param name="Enabled">
/// <see langword="true"/> to allow cycles to open, <see langword="false"/> to
/// stop them.
/// </param>
/// <seealso cref="Guild.IsOperational"/>
public sealed record SetGuildEnabled(ulong GuildId, ulong ActorId, bool Enabled) : ICommand;

/// <summary>
/// Checks the shape of a <see cref="SetGuildEnabled"/> command.
/// </summary>
/// <remarks>
/// There is nothing to assert about <see cref="SetGuildEnabled.Enabled"/> itself:
/// both values are legitimate, and which one is a change depends on state the
/// handler has and a validator does not.
/// </remarks>
internal sealed class SetGuildEnabledValidator : AbstractValidator<SetGuildEnabled>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public SetGuildEnabledValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(command => command.ActorId)
            .NotEqual(0uL)
            .WithMessage("An acting user is required.");
    }
}

/// <summary>
/// Carries out <see cref="SetGuildEnabled"/>.
/// </summary>
/// <param name="guilds">The guild store.</param>
/// <param name="members">Used to confirm the actor may configure the guild.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">Supplies the current instant.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class SetGuildEnabledHandler(
    IGuildRepository guilds,
    IGuildMemberLookup members,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<SetGuildEnabledHandler> logger)
    : ICommandHandler<SetGuildEnabled>
{
    /// <inheritdoc/>
    public async Task<Result> HandleAsync(
        SetGuildEnabled request,
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

        DateTimeOffset now = clock.GetUtcNow();
        Guild guild = await guilds.GetOrCreateAsync(
            request.GuildId,
            now,
            request.ActorId,
            cancellationToken);

        if (guild.IsEnabled == request.Enabled)
        {
            // Already in the requested state. Writing an audit row here would
            // fill the trail with entries recording that nothing happened,
            // which is exactly the noise that makes a trail unreadable.
            return Result.Success();
        }

        guild.SetEnabled(request.Enabled, now, request.ActorId);

        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Guild,
            request.Enabled ? AuditAction.Enabled : AuditAction.Disabled,
            now,
            request.ActorId));

        GuildLog.EnabledChanged(
            logger,
            request.GuildId,
            request.Enabled ? "on" : "off",
            request.ActorId);

        return Result.Success();
    }
}
