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
/// Assigns one channel to one of the jobs a guild routes through, or clears it.
/// </summary>
/// <remarks>
/// One role per command, rather than a request carrying all five channels with
/// <see langword="null"/> meaning "leave this one alone". That older shape existed
/// to mirror a slash command with five optional options, and it made clearing a
/// channel inexpressible: absence already meant "no change", so there was no value
/// left over to mean "remove it".
/// <br/>
/// Naming the role instead separates the two intentions cleanly. A
/// <see langword="null"/> <paramref name="ChannelId"/> now unambiguously means
/// clear, because the role says which setting is being talked about.
/// </remarks>
/// <param name="GuildId">The guild being configured.</param>
/// <param name="ActorId">The user issuing the change.</param>
/// <param name="Role">Which job the channel is being assigned to.</param>
/// <param name="ChannelId">
/// The channel to assign, or <see langword="null"/> to clear the role.
/// </param>
public sealed record SetChannel(
    ulong GuildId,
    ulong ActorId,
    GuildChannelRole Role,
    ulong? ChannelId) : ICommand;

/// <summary>
/// Checks the shape of a <see cref="SetChannel"/> command.
/// </summary>
/// <remarks>
/// Only what can be judged from the command alone. Whether the bot can actually
/// read or post in a channel needs Discord, and is checked when it tries.
/// </remarks>
internal sealed class SetChannelValidator : AbstractValidator<SetChannel>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public SetChannelValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(command => command.ActorId)
            .NotEqual(0uL)
            .WithMessage("An acting user is required.");

        RuleFor(command => command.Role)
            .IsInEnum()
            .WithMessage("That is not a channel this bot uses.");

        RuleFor(command => command.ChannelId)
            .NotEqual(0uL)
            .WithMessage("A channel is required. Omit it entirely to clear the setting.");

        // Intake and review are what IsReady is made of, so clearing one takes
        // the guild out of service. That is a legitimate thing to want, but it
        // is not something to reach by accident, and the caller has a disable
        // command that says so out loud.
        RuleFor(command => command)
            .Must(command => command.ChannelId is not null
                || command.Role is not (GuildChannelRole.Intake or GuildChannelRole.Review))
            .WithMessage(
                "The intake and review channels cannot be cleared, only pointed somewhere else. "
                + "Use the disable command to stop cycles opening.");
    }
}

/// <summary>
/// Carries out <see cref="SetChannel"/>.
/// </summary>
/// <param name="guilds">The guild store.</param>
/// <param name="members">Used to confirm the actor may configure the guild.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">Supplies the current instant.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class SetChannelHandler(
    IGuildRepository guilds,
    IGuildMemberLookup members,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<SetChannelHandler> logger)
    : ICommandHandler<SetChannel>
{
    /// <inheritdoc/>
    public async Task<Result> HandleAsync(SetChannel request, CancellationToken cancellationToken)
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

        GuildChannels current = guild.Channels;
        GuildChannels updated = Apply(current, request.Role, request.ChannelId);

        if (updated.IntakeChannelId is { } intake
            && updated.ReviewChannelId is { } review
            && intake == review)
        {
            return GuildErrors.ChannelConflict;
        }

        if (updated == current)
        {
            // Nothing changed, so there is nothing to audit and no reason to
            // write a row. Reported as success because the caller's intent is
            // satisfied: the channel is what they asked for.
            return Result.Success();
        }

        guild.ConfigureChannels(updated, now, request.ActorId);

        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Guild,
            AuditAction.ChannelsConfigured,
            now,
            request.ActorId,
            metadata: Describe(current, updated)));

        GuildLog.ChannelsConfigured(
            logger,
            request.GuildId,
            request.ActorId,
            updated.IntakeChannelId,
            updated.ReviewChannelId);

        return Result.Success();
    }

    private static GuildChannels Apply(
        GuildChannels channels,
        GuildChannelRole role,
        ulong? channelId) => role switch
        {
            GuildChannelRole.Intake => channels with { IntakeChannelId = channelId },
            GuildChannelRole.Review => channels with { ReviewChannelId = channelId },
            GuildChannelRole.Results => channels with { ResultsChannelId = channelId },
            GuildChannelRole.Archive => channels with { ArchiveChannelId = channelId },
            GuildChannelRole.Log => channels with { LogChannelId = channelId },
            _ => channels,
        };

    private static string Describe(GuildChannels before, GuildChannels after)
        => System.Text.Json.JsonSerializer.Serialize(new { before, after });
}
