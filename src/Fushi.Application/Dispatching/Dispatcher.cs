using System.Collections.Concurrent;

using Fushi.Application.Abstractions.Messaging;

using Microsoft.Extensions.DependencyInjection;

namespace Fushi.Application.Dispatching;

/// <summary>
/// The default <see cref="IDispatcher"/>, resolving handlers and behaviours from
/// the container.
/// </summary>
/// <remarks>
/// The awkward part of dispatching is that the caller knows the response type but
/// not the request type: <see cref="IDispatcher.SendAsync"/> receives an
/// <see cref="IRequest{TResponse}"/> whose concrete type is only available at run
/// time, while the handler interface needs both. Bridging that requires one
/// generic instantiation per request type.
/// <br/>
/// It is done once. The first request of each type constructs a small executor
/// closed over both types and caches it, after which dispatching is two dictionary
/// lookups and a virtual call. No reflection happens on the hot path, and none of
/// it appears in a handler.
/// </remarks>
/// <param name="services">
/// The scope to resolve from. A scope per request is what keeps one command's
/// database context from leaking into another's.
/// </param>
public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), Executor> Cache
        = new();

    /// <inheritdoc/>
    public Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Executor executor = Cache.GetOrAdd(
            (request.GetType(), typeof(TResponse)),
            static key => Create(key.Request, key.Response));

        return ((Executor<TResponse>)executor).ExecuteAsync(services, request, cancellationToken);
    }

    private static Executor Create(Type requestType, Type responseType)
    {
        Type executorType = typeof(Executor<,>).MakeGenericType(requestType, responseType);

        return (Executor)Activator.CreateInstance(executorType)!;
    }

    private abstract class Executor;

    private abstract class Executor<TResponse> : Executor
    {
        public abstract Task<TResponse> ExecuteAsync(
            IServiceProvider services,
            IRequest<TResponse> request,
            CancellationToken cancellationToken);
    }

    private sealed class Executor<TRequest, TResponse> : Executor<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> ExecuteAsync(
            IServiceProvider services,
            IRequest<TResponse> request,
            CancellationToken cancellationToken)
        {
            IRequestHandler<TRequest, TResponse> handler =
                services.GetService<IRequestHandler<TRequest, TResponse>>()
                ?? throw new InvalidOperationException(
                    $"No handler is registered for '{typeof(TRequest)}'. Every request needs "
                    + "exactly one handler; check that its assembly is passed to "
                    + "AddApplication.");

            var typed = (TRequest)request;

            RequestHandlerDelegate<TResponse> pipeline =
                () => handler.HandleAsync(typed, cancellationToken);

            // Wrapping in reverse registration order makes the first-registered
            // behaviour the outermost, so the host's registration sequence reads
            // the same way the request travels.
            foreach (IPipelineBehavior<TRequest, TResponse> behavior in
                services.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
            {
                RequestHandlerDelegate<TResponse> next = pipeline;
                IPipelineBehavior<TRequest, TResponse> current = behavior;

                pipeline = () => current.HandleAsync(typed, next, cancellationToken);
            }

            return pipeline();
        }
    }
}
