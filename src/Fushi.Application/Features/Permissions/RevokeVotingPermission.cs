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

namespace Fushi.Application.Features.Permissions;

/// <summary>
/// Withdraws voting rights from a user or a role.
/// </summary>
/// <remarks>
/// The grant is soft-deleted rather than removed. Votes already cast under it have
/// to remain explainable, and "who was allowed to vote at the time" is a question
/// that gets asked precisely when somebody's rights have since been taken away —
/// deleting the row would erase the answer at the moment it became interesting.
/// <br/>
/// Because grants are additive with no deny rule, revoking one does not
/// necessarily stop the target voting: a user who also holds a granted role keeps
/// their rights through that role. That is the intended reading of an additive
/// model, and the display of the grant list is what makes it visible.
/// </remarks>
/// <param name="GuildId">The guild the grant applies in.</param>
/// <param name="ActorId">The user issuing the revocation.</param>
/// <param name="Scope">Whether a user grant or a role grant is being withdrawn.</param>
/// <param name="TargetId">The snowflake of the user or role being revoked.</param>
/// <seealso cref="GrantVotingPermission"/>
public sealed record RevokeVotingPermission(
    ulong GuildId,
    ulong ActorId,
    VotingPermissionScope Scope,
    ulong TargetId) : ICommand;

/// <summary>
/// Checks the shape of a <see cref="RevokeVotingPermission"/> command.
/// </summary>
/// <remarks>
/// Whether the grant exists is state, so the handler answers that. The scope is
/// checked here because an undefined value cannot match any stored grant and would
/// be reported as "nothing to revoke", which sends the user looking for a problem
/// in the wrong place.
/// </remarks>
internal sealed class RevokeVotingPermissionValidator : AbstractValidator<RevokeVotingPermission>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public RevokeVotingPermissionValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(command => command.ActorId)
            .NotEqual(0uL)
            .WithMessage("An acting user is required.");

        RuleFor(command => command.TargetId)
            .NotEqual(0uL)
            .WithMessage("Pick the user or role to withdraw voting rights from.");

        RuleFor(command => command.Scope)
            .IsInEnum()
            .WithMessage("A grant targets either a user or a role.");
    }
}

/// <summary>
/// Carries out <see cref="RevokeVotingPermission"/>.
/// </summary>
/// <param name="guilds">The guild store.</param>
/// <param name="members">Used to confirm the actor may configure the guild.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">Supplies the current instant.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class RevokeVotingPermissionHandler(
    IGuildRepository guilds,
    IGuildMemberLookup members,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<RevokeVotingPermissionHandler> logger)
    : ICommandHandler<RevokeVotingPermission>
{
    /// <summary>
    /// Withdraws the grant, or reports that there was none to withdraw.
    /// </summary>
    /// <remarks>
    /// The grant is located before it is revoked so that its identifier can go on
    /// the audit entry. Recording only the target snowflake would leave the trail
    /// unable to say which of a series of grants and revocations for the same user
    /// this entry refers to.
    /// </remarks>
    /// <param name="request">The command to carry out.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The outcome of the request.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> is <see langword="null"/>.
    /// </exception>
    public async Task<Result> HandleAsync(
        RevokeVotingPermission request,
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

        VotingPermission? grant = guild.FindGrant(request.Scope, request.TargetId);
        if (grant is null)
        {
            return PermissionErrors.NotFound;
        }

        DateTimeOffset now = clock.GetUtcNow();

        // The return value only restates what FindGrant already established, so
        // it is discarded rather than checked twice.
        _ = guild.Revoke(request.Scope, request.TargetId, now, request.ActorId);

        // The note goes in the metadata, not the reason. It was written to justify
        // the grant, and recording it as the reason would attribute the granter's
        // words to whoever revoked it — the one reading this trail later is trying
        // to establish exactly that distinction.
        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Permission,
            AuditAction.PermissionRevoked,
            now,
            request.ActorId,
            subjectId: grant.Id,
            targetId: request.TargetId,
            metadata: grant.Note is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(new { grantNote = grant.Note })));

        // Named through nameof rather than ToString, so the log argument is a
        // compile-time constant and nothing is formatted on the way to a logger
        // that may well discard the line.
        string scope = request.Scope switch
        {
            VotingPermissionScope.User => nameof(VotingPermissionScope.User),
            VotingPermissionScope.Role => nameof(VotingPermissionScope.Role),
            _ => nameof(VotingPermissionScope.User),
        };

        GuildLog.PermissionRevoked(
            logger,
            request.GuildId,
            scope,
            request.TargetId,
            request.ActorId);

        return Result.Success();
    }
}
