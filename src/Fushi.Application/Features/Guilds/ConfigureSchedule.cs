using Fushi.Application.Abstractions.Discord;
using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Application.Errors;
using Fushi.Application.Logging;
using Fushi.Core.Entities.Audits;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Results;

using FluentValidation;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Features.Guilds;

/// <summary>
/// Sets the recurring schedule a guild's voting cycles run on.
/// </summary>
/// <remarks>
/// As with <see cref="ConfigureVotingPolicy"/>, <see langword="null"/> means "leave
/// this as it was", because Discord does not send slash command options the user
/// left blank and treating absence as an instruction would reset the rest of the
/// schedule on every partial edit.
/// <br/>
/// The times are wall-clock times in the guild's own zone, not instants, which is
/// how the people running the server think about them: "Monday, Wednesday and
/// Saturday, ten in the morning until ten at night". Turning that into absolute
/// instants is <see cref="CycleSchedule"/>'s job, and it has to be redone per
/// date because a fixed offset cannot express a local working day across a
/// daylight saving change.
/// </remarks>
/// <param name="GuildId">The guild being configured.</param>
/// <param name="ActorId">The user issuing the change.</param>
/// <param name="Days">
/// The days of the week cycles open on, or <see langword="null"/> to leave them.
/// <see cref="CycleDays.None"/> is refused: a schedule that runs on no day can
/// never open a cycle, so accepting it would switch the bot off through a setting
/// that does not look like an off switch. Use <c>/config enable</c> for that.
/// </param>
/// <param name="OpensAt">
/// The local time voting opens, or <see langword="null"/> to leave it.
/// </param>
/// <param name="ClosesAt">
/// The local time voting closes, or <see langword="null"/> to leave it. A time at
/// or before <paramref name="OpensAt"/> is read as closing the following day,
/// which is how an overnight window is expressed.
/// </param>
/// <param name="TimeZoneId">
/// The IANA identifier the times are expressed in, such as <c>Europe/Berlin</c>,
/// or <see langword="null"/> to leave it.
/// </param>
/// <seealso cref="CycleSchedule"/>
public sealed record ConfigureSchedule(
    ulong GuildId,
    ulong ActorId,
    CycleDays? Days = null,
    TimeOnly? OpensAt = null,
    TimeOnly? ClosesAt = null,
    string? TimeZoneId = null) : ICommand;

/// <summary>
/// Checks the shape of a <see cref="ConfigureSchedule"/> command.
/// </summary>
/// <remarks>
/// Whether the time zone identifier actually resolves is not checked here. That
/// answer depends on the machine's time zone database rather than on the request,
/// so it belongs to the handler; see
/// <see cref="ConfigureScheduleHandler.HandleAsync"/>.
/// </remarks>
internal sealed class ConfigureScheduleValidator : AbstractValidator<ConfigureSchedule>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public ConfigureScheduleValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(command => command.ActorId)
            .NotEqual(0uL)
            .WithMessage("An acting user is required.");

        RuleFor(command => command.Days)
            .NotEqual(CycleDays.None)
            .When(command => command.Days is not null)
            .WithMessage("Pick at least one day, or use /config enable to pause voting.");

        // A [Flags] enum accepts any integer, so a value can carry bits that name
        // no day at all. Rejecting those keeps a mistranslated option from being
        // stored as a schedule nobody can read back.
        RuleFor(command => command.Days)
            .Must(days => (days!.Value & ~CycleDays.Daily) == CycleDays.None)
            .When(command => command.Days is not null)
            .WithMessage("That combination of days is not one this bot recognises.");

        RuleFor(command => command.TimeZoneId)
            .NotEmpty()
            .When(command => command.TimeZoneId is not null)
            .WithMessage("Give a time zone name such as Europe/Berlin, or leave it out.");

        RuleFor(command => command)
            .Must(command => command.Days is not null
                || command.OpensAt is not null
                || command.ClosesAt is not null
                || command.TimeZoneId is not null)
            .WithMessage("Give at least one part of the schedule to change.");
    }
}

/// <summary>
/// Carries out <see cref="ConfigureSchedule"/>.
/// </summary>
/// <param name="guilds">The guild store.</param>
/// <param name="members">Used to confirm the actor may configure the guild.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">Supplies the current instant.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class ConfigureScheduleHandler(
    IGuildRepository guilds,
    IGuildMemberLookup members,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<ConfigureScheduleHandler> logger)
    : ICommandHandler<ConfigureSchedule>
{
    /// <summary>
    /// Applies the requested schedule, once the actor's authority and the time
    /// zone have both been established.
    /// </summary>
    /// <remarks>
    /// <see cref="Guild.ConfigureSchedule"/> refuses a zone this machine cannot
    /// resolve, and does so by throwing. Checking the identifier here as well is
    /// not redundant: it turns what would surface as an internal error into
    /// <see cref="GuildErrors.UnknownTimeZone"/>, which tells the user their
    /// identifier was wrong and what a correct one looks like. The entity keeps
    /// its own guard because it cannot assume every caller is this handler.
    /// </remarks>
    /// <param name="request">The command to carry out.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The outcome of the request.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> is <see langword="null"/>.
    /// </exception>
    public async Task<Result> HandleAsync(
        ConfigureSchedule request,
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

        CycleSchedule current = guild.Schedule;
        string timeZoneId = request.TimeZoneId ?? current.TimeZoneId;

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
        {
            return GuildErrors.UnknownTimeZone(timeZoneId);
        }

        CycleSchedule updated = new(
            request.Days ?? current.Days,
            request.OpensAt ?? current.OpensAt,
            request.ClosesAt ?? current.ClosesAt,
            timeZoneId);

        if (updated == current)
        {
            // Nothing changed, so there is nothing to audit and no reason to
            // write a row. Reported as success because the caller's intent is
            // satisfied: the schedule is what they asked for.
            return Result.Success();
        }

        guild.ConfigureSchedule(updated, now, request.ActorId);

        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Guild,
            AuditAction.ScheduleConfigured,
            now,
            request.ActorId,
            metadata: Describe(current, updated)));

        // Naming the days is the one part of this log line that costs something:
        // formatting a [Flags] enum concatenates one string per bit, and unlike a
        // single-valued enum there is no constant to pass instead. The level is
        // therefore checked before the argument is built rather than left to the
        // generated method, which would discard it after the fact.
        if (logger.IsEnabled(LogLevel.Information))
        {
            string days = updated.Days.ToString();

            GuildLog.ScheduleConfigured(
                logger,
                request.GuildId,
                request.ActorId,
                days,
                updated.OpensAt,
                updated.ClosesAt,
                updated.TimeZoneId);
        }

        return Result.Success();
    }

    private static string Describe(CycleSchedule before, CycleSchedule after)
        => System.Text.Json.JsonSerializer.Serialize(
            new { before = before.ToString(), after = after.ToString() });
}
