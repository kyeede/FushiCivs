using Fushi.Core.Results;

namespace Fushi.Application.Abstractions.Messaging;

/// <summary>
/// A request that reads state without changing it.
/// </summary>
/// <remarks>
/// Marking a request as a query is a claim the pipeline acts on: no transaction
/// is opened for it, and the persistence layer is free to serve it from a
/// no-tracking read. A handler that mutates anything while implementing this
/// interface breaks both assumptions silently, so it must not.
/// </remarks>
/// <typeparam name="TResponse">The type of value read.</typeparam>
/// <example>
/// <code>
/// public sealed record GetSubmission(ulong GuildId, ShortCode Code)
///     : IQuery&lt;SubmissionModel&gt;;
/// </code>
/// </example>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
