namespace Fushi.Application.Abstractions.Messaging;

/// <summary>
/// Routes a request to its handler, through the pipeline.
/// </summary>
/// <remarks>
/// The single entry point into this layer. A Discord interaction module, a
/// hosted service, and a test all reach the same handler the same way, which is
/// what keeps the scheduler and the slash command from drifting apart in
/// behaviour.
/// <br/>
/// Deliberately not a mediator in the full sense: there is no publish, no
/// notification, and no multiple-handler dispatch. One request has one handler.
/// Anything that needs to fan out does so by sending further requests
/// explicitly, where the sequence is visible in the code rather than implied by
/// registration order.
/// </remarks>
public interface IDispatcher
{
    /// <summary>
    /// Sends a request through the pipeline to its handler.
    /// </summary>
    /// <remarks>
    /// The response type is inferred from the request, so
    /// <c>SendAsync(new GetSubmission(...), token)</c> returns
    /// <c>Task&lt;Result&lt;SubmissionModel&gt;&gt;</c> without the caller
    /// naming it.
    /// </remarks>
    /// <typeparam name="TResponse">
    /// The response type declared by the request.
    /// </typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">
    /// Cancelled when the caller stops waiting.
    /// </param>
    /// <returns>The outcome of the request.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No handler is registered for the request type. This is a composition
    /// error rather than a runtime condition, and is why the host verifies the
    /// registry on startup instead of discovering it on first use.
    /// </exception>
    Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}
