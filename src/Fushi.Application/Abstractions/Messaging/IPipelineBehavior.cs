namespace Fushi.Application.Abstractions.Messaging;

/// <summary>
/// Invokes the next stage of the pipeline, ending at the handler itself.
/// </summary>
/// <remarks>
/// Takes no arguments because the request and the cancellation token are already
/// captured by the stage that built this delegate. A behaviour that wants to
/// change the request cannot do so by passing a different one; it should fail
/// the request instead.
/// </remarks>
/// <typeparam name="TResponse">The response type produced.</typeparam>
/// <returns>The response from the remainder of the pipeline.</returns>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// A stage wrapped around every handler, for concerns that apply to all of them.
/// </summary>
/// <remarks>
/// Behaviours run in registration order on the way in and unwind in reverse on
/// the way out, so the first one registered is the outermost. The order the host
/// registers them in is deliberate:
/// <br/>
/// Logging is outermost, so that a request rejected by validation still appears
/// in the log with its duration. Validation comes next, so that a malformed
/// request is refused before a transaction is opened for it. The unit of work is
/// innermost, wrapping only the handler, so a transaction lives no longer than
/// the work that needs it.
/// </remarks>
/// <typeparam name="TRequest">The request type this stage applies to.</typeparam>
/// <typeparam name="TResponse">The response type produced.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Runs this stage.
    /// </summary>
    /// <param name="request">The request passing through.</param>
    /// <param name="next">
    /// The remainder of the pipeline. Not calling it short-circuits the request,
    /// which is how validation refuses one.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancelled when the caller stops waiting.
    /// </param>
    /// <returns>
    /// The response, either from <paramref name="next"/> or synthesised by this
    /// stage.
    /// </returns>
    Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
