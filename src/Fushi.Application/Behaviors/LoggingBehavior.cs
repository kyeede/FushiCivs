using System.Diagnostics;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Logging;
using Fushi.Core.Errors;
using Fushi.Core.Results;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Behaviors;

/// <summary>
/// Records what every request did and how long it took.
/// </summary>
/// <remarks>
/// Registered outermost, so the timing it reports covers validation and the
/// commit as well as the handler. A request rejected by validation therefore
/// still appears in the log, which matters when diagnosing a command that users
/// report as "doing nothing".
/// <br/>
/// Cancellation is re-thrown without being logged as a fault. An interaction
/// whose deadline passed is expected behaviour under load, and treating it as an
/// error would bury the faults that matter.
/// </remarks>
/// <typeparam name="TRequest">The request type passing through.</typeparam>
/// <typeparam name="TResponse">The response type produced.</typeparam>
/// <param name="logger">The logger to write to.</param>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;

    /// <inheritdoc/>
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        PipelineLog.Dispatching(logger, RequestName);

        long startedAt = Stopwatch.GetTimestamp();
        try
        {
            TResponse response = await next();

            PipelineLog.Outcome(
                logger,
                RequestName,
                ElapsedSince(startedAt),
                response is IResult result ? result.Error : Error.None);

            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            PipelineLog.Faulted(logger, RequestName, ElapsedSince(startedAt), exception);
            throw;
        }
    }

    private static long ElapsedSince(long timestamp)
        => (long)Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
}
