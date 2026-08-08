using Fushi.Application.Abstractions.Discord;
using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence;
using Fushi.Application.Errors;
using Fushi.Application.Logging;
using Fushi.Core.Entities.Audits;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Results;

using FluentValidation;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Features.Guilds;

/// <summary>
/// Sets the rules that turn a guild's votes into decisions.
/// </summary>
/// <remarks>
/// Every setting is nullable and <see langword="null"/> means "leave it alone",
/// for the same reason it does on <see cref="ConfigureChannels"/>: Discord omits
/// slash command options the user did not fill in, so reading absence as an
/// instruction would reset the other four settings every time somebody adjusted
/// one.
/// <br/>
/// The new rules apply to cycles opened afterwards only. A cycle already taking
/// votes keeps the policy it opened under, so the bar cannot be moved while
/// people are voting against it. That guarantee lives on
/// <see cref="Guild.ConfigureVoting"/>; this command simply relies on it.
/// </remarks>
/// <param name="GuildId">The guild being configured.</param>
/// <param name="ActorId">The user issuing the change.</param>
/// <param name="ApprovalRatio">
/// The share of deciding votes that must be approvals, expressed as a fraction
/// rather than a percentage: 60% is <c>0.60</c>, not <c>60</c>. Anything above
/// <c>1</c> is rejected rather than silently clamped, because a user who typed
/// <c>60</c> meaning 60% has made a mistake worth telling them about.
/// <see langword="null"/> leaves the current ratio in place.
/// </param>
/// <param name="Quorum">
/// The minimum number of deciding votes for a result to count, or
/// <see langword="null"/> to leave it. Falling short produces
/// <c>Skipped</c> rather than a rejection.
/// </param>
/// <param name="AllowAbstain">
/// Whether voters may abstain, or <see langword="null"/> to leave it. An
/// abstention is recorded as participation but counts towards neither the ratio
/// nor the quorum.
/// </param>
/// <param name="AllowSelfVote">
/// Whether an applicant may vote on their own submission, or
/// <see langword="null"/> to leave it.
/// </param>
/// <param name="AllowVoteChange">
/// Whether a voter may revise their vote while the cycle is open, or
/// <see langword="null"/> to leave it.
/// </param>
/// <seealso cref="VotingPolicy"/>
public sealed record ConfigureVotingPolicy(
    ulong GuildId,
    ulong ActorId,
    double? ApprovalRatio = null,
    int? Quorum = null,
    bool? AllowAbstain = null,
    bool? AllowSelfVote = null,
    bool? AllowVoteChange = null) : ICommand;

/// <summary>
/// Checks the shape of a <see cref="ConfigureVotingPolicy"/> command.
/// </summary>
/// <remarks>
/// The bounds here are tighter than the ones <see cref="VotingPolicy"/> itself
/// enforces, and deliberately so. The entity accepts a ratio of <c>0</c> and a
/// quorum of <c>0</c> because both are meaningful defaults internally — a zero
/// ratio reads back as the 60% default, and a zero quorum disables the gate. A
/// person typing a configuration command means neither of those things, so both
/// are refused before they can be mistaken for an instruction.
/// </remarks>
internal sealed class ConfigureVotingPolicyValidator : AbstractValidator<ConfigureVotingPolicy>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public ConfigureVotingPolicyValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(command => command.ActorId)
            .NotEqual(0uL)
            .WithMessage("An acting user is required.");

        RuleFor(command => command.ApprovalRatio)
            .Must(ratio => ratio is > 0d and <= 1d)
            .When(command => command.ApprovalRatio is not null)
            .WithMessage(
                "The approval ratio must be greater than 0 and at most 1. Give a percentage as "
                + "a fraction: 60% is 0.60.");

        RuleFor(command => command.Quorum)
            .GreaterThanOrEqualTo(1)
            .When(command => command.Quorum is not null)
            .WithMessage("The quorum must be at least one deciding vote.");

        RuleFor(command => command)
            .Must(command => command.ApprovalRatio is not null
                || command.Quorum is not null
                || command.AllowAbstain is not null
                || command.AllowSelfVote is not null
                || command.AllowVoteChange is not null)
            .WithMessage("Give at least one setting to change.");
    }
}

/// <summary>
/// Carries out <see cref="ConfigureVotingPolicy"/>.
/// </summary>
/// <param name="guilds">The guild store.</param>
/// <param name="members">Used to confirm the actor may configure the guild.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">Supplies the current instant.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class ConfigureVotingPolicyHandler(
    IGuildRepository guilds,
    IGuildMemberLookup members,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<ConfigureVotingPolicyHandler> logger)
    : ICommandHandler<ConfigureVotingPolicy>
{
    /// <inheritdoc/>
    public async Task<Result> HandleAsync(
        ConfigureVotingPolicy request,
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

        VotingPolicy current = guild.Policy;

        // VotingPolicy is a readonly record struct whose ratio and quorum are
        // held in private fields behind normalising properties, so a `with`
        // expression cannot reach them. Rebuilding through the constructor also
        // means the entity's own bounds checks run on the combined value rather
        // than only on the parts the caller happened to supply.
        VotingPolicy updated = new(
            request.ApprovalRatio ?? current.ApprovalRatio,
            request.Quorum ?? current.Quorum,
            request.AllowAbstain ?? current.AllowAbstain,
            request.AllowSelfVote ?? current.AllowSelfVote,
            request.AllowVoteChange ?? current.AllowVoteChange);

        if (updated == current)
        {
            // Nothing changed, so there is nothing to audit. Reported as success
            // because the caller's intent is satisfied: the rules are what they
            // asked for.
            return Result.Success();
        }

        guild.ConfigureVoting(updated, now, request.ActorId);

        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Guild,
            AuditAction.PolicyConfigured,
            now,
            request.ActorId,
            metadata: Describe(current, updated)));

        GuildLog.PolicyConfigured(
            logger,
            request.GuildId,
            request.ActorId,
            updated.ApprovalPercentage,
            updated.Quorum);

        return Result.Success();
    }

    private static string Describe(VotingPolicy before, VotingPolicy after)
        => System.Text.Json.JsonSerializer.Serialize(new { before, after });
}
