using Fushi.Core.Results;

namespace Fushi.Application.Abstractions.Messaging;

/// <summary>
/// A request that changes state and returns nothing beyond whether it worked.
/// </summary>
/// <remarks>
/// Commands run inside a transaction that the pipeline opens and commits, so a
/// handler may make several changes and rely on them being saved together or not
/// at all. Queries deliberately get no such treatment.
/// </remarks>
/// <example>
/// <code>
/// public sealed record SetGuildEnabled(ulong GuildId, bool Enabled, ulong ActorId) : ICommand;
/// </code>
/// </example>
public interface ICommand : IRequest<Result>;

/// <summary>
/// A request that changes state and returns a value describing what it did.
/// </summary>
/// <remarks>
/// Use this when the caller needs something that only exists after the change —
/// the short code of a newly captured submission, for instance. Where the caller
/// needs nothing back, prefer the non-generic <see cref="ICommand"/>.
/// </remarks>
/// <typeparam name="TResponse">The type of value produced on success.</typeparam>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;
