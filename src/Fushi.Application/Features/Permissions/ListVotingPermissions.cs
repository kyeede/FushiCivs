using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Application.Errors;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Results;
using Fushi.Core.Utilities.Paging;

using FluentValidation;

namespace Fushi.Application.Features.Permissions;

/// <summary>
/// Lists the live voting grants for a guild.
/// </summary>
/// <remarks>
/// Withdrawn grants are left out. They are kept in the database so that votes cast
/// under them stay explainable, but this query answers "who can vote here now",
/// and mixing the two would make the list longer than the set of people it
/// describes.
/// </remarks>
/// <param name="GuildId">The guild whose grants are wanted.</param>
/// <param name="Paging">
/// Which page of the list to return. Built with <see cref="PageRequest.Clamp"/>
/// when it comes from a slash command, so that a nonsensical page number is
/// corrected rather than refused.
/// </param>
/// <seealso cref="VotingPermissionModel"/>
public sealed record ListVotingPermissions(ulong GuildId, PageRequest Paging)
    : IQuery<Page<VotingPermissionModel>>;

/// <summary>
/// One live voting grant, arranged for display.
/// </summary>
/// <remarks>
/// The mention is resolved here rather than left to the renderer, because the
/// markup that produces a link differs between a user and a role and getting it
/// wrong yields literal text that still looks correct in source.
/// </remarks>
/// <param name="Id">
/// The grant's internal identifier, carried so that a subsequent action can refer
/// to this exact grant rather than to whatever currently matches the target.
/// </param>
/// <param name="Scope">Whether the grant covers a user or a role.</param>
/// <param name="TargetId">The snowflake of the granted user or role.</param>
/// <param name="Mention">
/// Discord markup that renders as a link to the target.
/// </param>
/// <param name="Note">
/// The reason recorded when the grant was made, or <see langword="null"/> when
/// none was given.
/// </param>
/// <param name="GrantedAt">The instant the grant was made.</param>
/// <param name="GrantedBy">
/// The snowflake of the actor that made the grant, or <c>0</c> when the bot made
/// it on its own.
/// </param>
public sealed record VotingPermissionModel(
    Guid Id,
    VotingPermissionScope Scope,
    ulong TargetId,
    string Mention,
    string? Note,
    DateTimeOffset GrantedAt,
    ulong GrantedBy);

/// <summary>
/// Checks the shape of a <see cref="ListVotingPermissions"/> query.
/// </summary>
/// <remarks>
/// <see cref="PageRequest"/> validates itself when it is constructed, but its
/// default value bypasses that constructor and carries a page number and size of
/// zero. Catching it here turns what would be an exception from
/// <see cref="PageInfo"/> at the end of the handler into a message the caller can
/// act on.
/// </remarks>
internal sealed class ListVotingPermissionsValidator : AbstractValidator<ListVotingPermissions>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public ListVotingPermissionsValidator()
    {
        RuleFor(query => query.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(query => query.Paging.Number)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page numbers start at 1.");

        RuleFor(query => query.Paging.Size)
            .InclusiveBetween(1, PageRequest.MAX_SIZE)
            .WithMessage($"A page may hold between 1 and {PageRequest.MAX_SIZE} grants.");
    }
}

/// <summary>
/// Carries out <see cref="ListVotingPermissions"/>.
/// </summary>
/// <param name="guilds">The guild store.</param>
internal sealed class ListVotingPermissionsHandler(IGuildRepository guilds)
    : IQueryHandler<ListVotingPermissions, Page<VotingPermissionModel>>
{
    /// <summary>
    /// Reads the guild's live grants and returns the requested page of them.
    /// </summary>
    /// <remarks>
    /// Role grants are listed before user grants, and each group in the order it
    /// was granted. A role grant covers everybody who holds the role, so it is the
    /// larger fact about who can vote, and a moderator auditing access wants the
    /// broad rules before the individual exceptions rather than having to pick them
    /// out of a list sorted by time alone.
    /// <br/>
    /// The paging is applied in memory. The grants are already loaded — the guild
    /// cannot be checked for duplicates or evaluated for a vote without them — and
    /// the set is bounded by how many roles and people a server has, so pushing the
    /// skip and take into the database would buy a round trip and nothing else.
    /// </remarks>
    /// <param name="request">The query to carry out.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>
    /// The requested page of grants, or a failure when the guild has no row.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> is <see langword="null"/>.
    /// </exception>
    public async Task<Result<Page<VotingPermissionModel>>> HandleAsync(
        ListVotingPermissions request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guild? guild = await guilds.FindWithPermissionsAsync(request.GuildId, cancellationToken);
        if (guild is null)
        {
            return GuildErrors.NotFound;
        }

        IReadOnlyList<VotingPermission> grants = guild.LiveGrants();

        VotingPermissionModel[] items =
        [
            .. grants
                .OrderBy(static grant => grant.Scope == VotingPermissionScope.Role ? 0 : 1)
                .ThenBy(static grant => grant.CreatedAt)
                .Skip(request.Paging.Skip)
                .Take(request.Paging.Size)
                .Select(static grant => new VotingPermissionModel(
                    grant.Id,
                    grant.Scope,
                    grant.TargetId,
                    grant.Mention,
                    grant.Note,
                    grant.CreatedAt,
                    grant.CreatedBy)),
        ];

        return Page<VotingPermissionModel>.From(items, request.Paging, grants.Count);
    }
}
