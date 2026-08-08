using Fushi.Core.Results;

namespace Fushi.Application.Abstractions.Messaging;

/// <summary>
/// Carries out one kind of request.
/// </summary>
/// <remarks>
/// Exactly one handler is registered per request type. That is what replaces the
/// "service" class: instead of a <c>SubmissionService</c> accumulating a dozen
/// loosely related methods and the union of all their dependencies, each
/// operation is its own class taking only what it actually needs.
/// <br/>
/// Prefer <see cref="ICommandHandler{TCommand}"/>,
/// <see cref="ICommandHandler{TCommand, TResponse}"/>, or
/// <see cref="IQueryHandler{TQuery, TResponse}"/>, which say which kind of
/// operation is being implemented. Registration discovers this base interface,
/// so all three are found by the same scan.
/// </remarks>
/// <typeparam name="TRequest">The request type handled.</typeparam>
/// <typeparam name="TResponse">The response type produced.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Carries out the request.
    /// </summary>
    /// <remarks>
    /// Expected failures should be returned as a failed result, not thrown. An
    /// exception escaping a handler is treated as a fault: it aborts the
    /// transaction and is reported to the user as an internal error rather than
    /// as something they can correct.
    /// </remarks>
    /// <param name="request">The request to carry out.</param>
    /// <param name="cancellationToken">
    /// Cancelled when the caller stops waiting, which for an interaction means
    /// Discord's response deadline has passed.
    /// </param>
    /// <returns>The outcome of the request.</returns>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Carries out a command that returns nothing beyond whether it worked.
/// </summary>
/// <typeparam name="TCommand">The command type handled.</typeparam>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

/// <summary>
/// Carries out a command that returns a value.
/// </summary>
/// <typeparam name="TCommand">The command type handled.</typeparam>
/// <typeparam name="TResponse">The type of value produced.</typeparam>
public interface ICommandHandler<in TCommand, TResponse>
    : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

/// <summary>
/// Carries out a query.
/// </summary>
/// <typeparam name="TQuery">The query type handled.</typeparam>
/// <typeparam name="TResponse">The type of value read.</typeparam>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
