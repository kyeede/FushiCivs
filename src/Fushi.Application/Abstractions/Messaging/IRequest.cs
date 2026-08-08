namespace Fushi.Application.Abstractions.Messaging;

/// <summary>
/// Something the application can be asked to do, and the type of answer it
/// gives.
/// </summary>
/// <remarks>
/// The shared base of <see cref="ICommand"/> and <see cref="IQuery{TResponse}"/>.
/// Carrying the response type on the request is what lets
/// <see cref="IDispatcher.SendAsync"/> infer its own return type from its
/// argument, so a caller never has to restate it and can never state it wrongly.
/// <br/>
/// Implement <see cref="ICommand"/> or <see cref="IQuery{TResponse}"/> rather
/// than this interface directly; the distinction between changing state and
/// reading it is what the pipeline keys its behaviour off.
/// </remarks>
/// <typeparam name="TResponse">
/// The type returned when the request is handled. Always a
/// <see cref="Fushi.Core.Results.Result"/> or
/// <see cref="Fushi.Core.Results.Result{T}"/>, so that an expected failure
/// arrives as a value rather than as an exception.
/// </typeparam>
public interface IRequest<out TResponse>;
