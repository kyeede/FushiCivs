using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence;
using Fushi.Application.Logging;
using Fushi.Core.Results;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Behaviors;

/// <summary>
/// Commits a command's changes once, after it succeeds.
/// </summary>
/// <remarks>
/// Handlers do not save. They mutate entities and return a result, and this stage
/// decides what becomes permanent. Two things follow from that, both worth having:
/// a handler cannot leave a partial change committed by returning a failure
/// halfway through, and a handler cannot forget to save at all.
/// <br/>
/// Queries pass through untouched. Whether a request is a command is decided once
/// per closed generic type and cached in a static field, so the check costs
/// nothing per request.
/// </remarks>
/// <typeparam name="TRequest">The request type passing through.</typeparam>
/// <typeparam name="TResponse">The response type produced.</typeparam>
/// <param name="unitOfWork">The unit of work to commit through.</param>
/// <param name="logger">The logger to write to.</param>
public sealed class UnitOfWorkBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    ILogger<UnitOfWorkBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly bool IsCommand = DetectCommand();
    private static readonly string RequestName = typeof(TRequest).Name;

    /// <inheritdoc/>
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (!IsCommand)
        {
            return await next();
        }

        TResponse response = await next();

        if (response is IResult { IsFailure: true })
        {
            PipelineLog.RolledBack(logger, RequestName);
            return response;
        }

        int rows = await unitOfWork.SaveChangesAsync(cancellationToken);
        PipelineLog.Committed(logger, rows, RequestName);

        return response;
    }

    private static bool DetectCommand()
    {
        if (typeof(ICommand).IsAssignableFrom(typeof(TRequest)))
        {
            return true;
        }

        foreach (Type contract in typeof(TRequest).GetInterfaces())
        {
            if (contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(ICommand<>))
            {
                return true;
            }
        }

        return false;
    }
}
