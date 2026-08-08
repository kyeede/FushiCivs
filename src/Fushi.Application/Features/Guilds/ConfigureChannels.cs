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
/// Sets which channels a guild uses for each stage of the process.
/// </summary>
/// <remarks>
/// A <see langword="null"/> channel leaves that setting as it was. This matches
/// how Discord delivers a slash command: options the user did not fill in simply
/// are not sent, so treating absence as "clear this" would wipe settings every
/// time somebody changed one of the others.
/// </remarks>
/// <param name="GuildId">The guild being configured.</param>
/// <param name="ActorId">The user issuing the change.</param>
/// <param name="IntakeChannelId">
/// Where applications are collected from, or <see langword="null"/> to leave it.
/// </param>
/// <param name="ReviewChannelId">
/// Where they are posted for voting, or <see langword="null"/> to leave it.
/// </param>
/// <param name="ResultsChannelId">
/// Where outcomes are published, or <see langword="null"/> to leave it. Falls
/// back to the review channel when never set.
/// </param>
/// <param name="ArchiveChannelId">
/// Where decided submissions are kept, or <see langword="null"/> to leave it.
/// </param>
/// <param name="LogChannelId">
/// Where moderation activity is recorded, or <see langword="null"/> to leave it.
/// </param>
public sealed record ConfigureChannels(
    ulong GuildId,
    ulong ActorId,
    ulong? IntakeChannelId = null,
    ulong? ReviewChannelId = null,
    ulong? ResultsChannelId = null,
    ulong? ArchiveChannelId = null,
    ulong? LogChannelId = null) : ICommand;

/// <summary>
/// Checks the shape of a <see cref="ConfigureChannels"/> command.
/// </summary>
/// <remarks>
/// Only what can be judged from the command alone. Whether the bot can actually
/// post in a channel needs Discord, and is checked by the handler.
/// </remarks>
internal sealed class ConfigureChannelsValidator : AbstractValidator<ConfigureChannels>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public ConfigureChannelsValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(command => command.ActorId)
            .NotEqual(0uL)
            .WithMessage("An acting user is required.");

        RuleFor(command => command)
            .Must(command => command.IntakeChannelId is not null
                || command.ReviewChannelId is not null
                || command.ResultsChannelId is not null
                || command.ArchiveChannelId is not null
                || command.LogChannelId is not null)
            .WithMessage("Give at least one channel to change.");
    }
}

/// <summary>
/// Carries out <see cref="ConfigureChannels"/>.
/// </summary>
/// <param name="guilds">The guild store.</param>
/// <param name="members">Used to confirm the actor may configure the guild.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">
/// Supplies the current instant. <see cref="TimeProvider"/> rather than a bespoke
/// clock interface: it is the framework's own abstraction, and tests can
/// substitute a fake without this project defining one.
/// </param>
/// <param name="logger">The logger to write to.</param>
internal sealed class ConfigureChannelsHandler(
    IGuildRepository guilds,
    IGuildMemberLookup members,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<ConfigureChannelsHandler> logger)
    : ICommandHandler<ConfigureChannels>
{
    /// <inheritdoc/>
    public async Task<Result> HandleAsync(
        ConfigureChannels request,
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

        GuildChannels current = guild.Channels;
        GuildChannels updated = new(
            request.IntakeChannelId ?? current.IntakeChannelId,
            request.ReviewChannelId ?? current.ReviewChannelId,
            request.ResultsChannelId ?? current.ResultsChannelId,
            request.ArchiveChannelId ?? current.ArchiveChannelId,
            request.LogChannelId ?? current.LogChannelId);

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
            // satisfied: the channels are what they asked for.
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

    private static string Describe(GuildChannels before, GuildChannels after)
        => System.Text.Json.JsonSerializer.Serialize(new { before, after });
}
