namespace Fushi.Gateway;

/// <summary>
/// The readiness signal, held as a single completion source for the lifetime of
/// the process.
/// </summary>
/// <remarks>
/// A <see cref="TaskCompletionSource"/> rather than an event or a polled flag: it
/// is the one shape that lets a caller who arrives late and a caller who arrives
/// early both wait correctly, with no window in which the signal has already
/// fired and the subscriber has not yet attached.
/// <br/>
/// Registered as a singleton separately from
/// <see cref="GatewayHostedService"/> so that a component can depend on the
/// signal without depending on the service that raises it, and so that reaching
/// the signal does not mean pulling a hosted service out of the container.
/// </remarks>
internal sealed class GatewayReadiness : IGatewayReadiness
{
    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc/>
    public bool IsReady => _ready.Task.IsCompletedSuccessfully;

    /// <inheritdoc/>
    public Task WaitForReadyAsync(CancellationToken cancellationToken = default)
        => _ready.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Records that the gateway has reported itself ready.
    /// </summary>
    /// <remarks>
    /// Idempotent. Discord raises <c>Ready</c> again after a session it could not
    /// resume, and the second one carries no new information for a waiter that
    /// was released by the first.
    /// </remarks>
    public void Signal() => _ready.TrySetResult();

    /// <summary>
    /// Records that the gateway will not become ready, releasing anybody waiting.
    /// </summary>
    /// <remarks>
    /// Cancels rather than faults. A faulted completion source that nobody awaits
    /// resurfaces later as an unobserved task exception, attributed to whatever
    /// happened to be running when the finaliser ran, which is a genuinely
    /// unpleasant thing to debug. Cancellation carries the same "stop waiting"
    /// meaning with none of that.
    /// </remarks>
    public void Abandon() => _ready.TrySetCanceled();
}
