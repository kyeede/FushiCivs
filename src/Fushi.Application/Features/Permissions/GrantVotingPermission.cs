using Fushi.Application.Abstractions.Discord;
using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Application.Errors;
using Fushi.Application.Logging;
using Fushi.Core.Entities.Audits;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Results;
using Fushi.Core.Utilities;

using FluentValidation;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Features.Permissions;

/// <summary>
/// Grants voting rights to a user, or to everyone holding a role.
/// </summary>
/// <remarks>
/// Voting is denied by default and opened by grant. Grants are additive and there
/// is no deny rule, so the effect of this command is only ever to widen who can
/// vote, and its effect can be reasoned about without knowing what else has been
/// configured.
/// <br/>
/// A role grant is resolved against the member's roles at the moment they try to
/// vote, so granting the role is the whole of the configuration: nobody has to be
/// added or removed here as the role's membership changes.
/// </remarks>
/// <param name="GuildId">The guild the grant applies in.</param>
/// <param name="ActorId">The user issuing the grant.</param>
/// <param name="Scope">Whether the grant targets a user or a role.</param>
/// <param name="TargetId">The snowflake of the user or role being granted.</param>
/// <param name="Note">
/// An optional reason, shown when the grant list is displayed and kept on the
/// audit entry. Worth recording: "why can this person vote" is asked long after
/// whoever decided has forgotten.
/// </param>
/// <seealso cref="VotingPermission"/>
public sealed record GrantVotingPermission(
    ulong GuildId,
    ulong ActorId,
    VotingPermissionScope Scope,
    ulong TargetId,
    string? Note = null) : ICommand;

/// <summary>
/// Checks the shape of a <see cref="GrantVotingPermission"/> command.
/// </summary>
/// <remarks>
/// Whether an equivalent grant already exists cannot be judged from the command,
/// so that is the handler's to establish. What can be judged is that the scope
/// names something real and that the note will fit on an audit entry, both of
/// which would otherwise surface as an exception from deep inside the domain
/// rather than as something the user can correct.
/// </remarks>
internal sealed class GrantVotingPermissionValidator : AbstractValidator<GrantVotingPermission>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public GrantVotingPermissionValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(command => command.ActorId)
            .NotEqual(0uL)
            .WithMessage("An acting user is required.");

        RuleFor(command => command.TargetId)
            .NotEqual(0uL)
            .WithMessage("Pick the user or role to grant voting rights to.");

        RuleFor(command => command.Scope)
            .IsInEnum()
            .WithMessage("A grant must target either a user or a role.");

        RuleFor(command => command.Note)
            .MaximumLength(AuditEntry.MAX_REASON_LENGTH)
            .WithMessage(
                $"A note may be at most {AuditEntry.MAX_REASON_LENGTH} characters long.");
    }
}

/// <summary>
/// Carries out <see cref="GrantVotingPermission"/>.
/// </summary>
/// <param name="guilds">The guild store.</param>
/// <param name="members">Used to confirm the actor may configure the guild.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">Supplies the current instant.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class GrantVotingPermissionHandler(
    IGuildRepository guilds,
    IGuildMemberLookup members,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<GrantVotingPermissionHandler> logger)
    : ICommandHandler<GrantVotingPermission>
{
    /// <summary>
    /// Adds the grant, unless an equivalent live one is already present.
    /// </summary>
    /// <remarks>
    /// The duplicate check is <see cref="Guild.Grant"/>'s own return value rather
    /// than a second search written here. What counts as an equivalent grant is a
    /// domain rule, and having it stated twice is how the two copies come to
    /// disagree.
    /// <br/>
    /// The guild is loaded with its grants attached, because a duplicate check
    /// against an unloaded collection would find nothing and cheerfully insert a
    /// second row that then has to be revoked twice.
    /// </remarks>
    /// <param name="request">The command to carry out.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The outcome of the request.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> is <see langword="null"/>.
    /// </exception>
    public async Task<Result> HandleAsync(
        GrantVotingPermission request,
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

        Guild? guild = await guilds.FindWithPermissionsAsync(request.GuildId, cancellationToken);
        if (guild is null)
        {
            return GuildErrors.NotFound;
        }

        DateTimeOffset now = clock.GetUtcNow();
        VotingPermission permission = VotingPermission.Create(
            guild.Id,
            request.Scope,
            request.TargetId,
            now,
            request.ActorId,
            request.Note);

        if (!guild.Grant(permission))
        {
            return PermissionErrors.AlreadyGranted(Mention(request.Scope, request.TargetId));
        }

        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Permission,
            AuditAction.PermissionGranted,
            now,
            request.ActorId,
            subjectId: permission.Id,
            targetId: request.TargetId,
            reason: request.Note));

        // Named through nameof rather than ToString, so the log argument is a
        // compile-time constant and nothing is formatted on the way to a logger
        // that may well discard the line.
        string scope = request.Scope switch
        {
            VotingPermissionScope.User => nameof(VotingPermissionScope.User),
            VotingPermissionScope.Role => nameof(VotingPermissionScope.Role),
            _ => nameof(VotingPermissionScope.User),
        };

        GuildLog.PermissionGranted(
            logger,
            request.GuildId,
            scope,
            request.TargetId,
            request.ActorId);

        return Result.Success();
    }

    private static string Mention(VotingPermissionScope scope, ulong targetId)
        => scope == VotingPermissionScope.Role
            ? MentionUtility.Role(targetId)
            : MentionUtility.User(targetId);
}
