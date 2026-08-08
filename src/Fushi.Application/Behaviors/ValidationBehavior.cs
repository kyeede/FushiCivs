using System.Reflection;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Logging;
using Fushi.Core.Errors;
using Fushi.Core.Results;

using FluentValidation;
using FluentValidation.Results;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Behaviors;

/// <summary>
/// Refuses a request whose shape is wrong before any handler sees it.
/// </summary>
/// <remarks>
/// Validators check the request in isolation: that a snowflake is non-zero, that
/// a ratio lies between nought and one, that a page size is sane. They cannot
/// check anything that needs the database, because they are given no way to reach
/// it. Rules of the form "this cycle is already open" belong to the handler,
/// which has the row in front of it.
/// <br/>
/// A request with no registered validator passes straight through. That is
/// deliberate rather than an oversight: a query taking nothing but a guild
/// snowflake has nothing worth asserting, and requiring an empty validator for it
/// would add a file per request that only ever said yes.
/// </remarks>
/// <typeparam name="TRequest">The request type passing through.</typeparam>
/// <typeparam name="TResponse">The response type produced.</typeparam>
/// <param name="validators">
/// Every validator registered for this request type. More than one is allowed, so
/// that a shared rule set can be composed with a request-specific one.
/// </param>
/// <param name="logger">The logger to write to.</param>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators,
    ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// The code carried by every validation failure, so that the presentation
    /// layer can recognise one without matching on its description.
    /// </summary>
    public const string FAILURE_CODE = "Validation.Failed";

    private static readonly string RequestName = typeof(TRequest).Name;

    // TResponse is a type parameter here, so refusing a request means producing a
    // failure of a type this method cannot name: it is either Result or Result<T>,
    // and only the latter takes a type argument. The reflection is done once for
    // the closed behaviour and reduced to a delegate.
    private static readonly Lazy<Func<Error, object>> FailureFactory = new(BuildFailureFactory);

    /// <inheritdoc/>
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        IValidator<TRequest>[] applicable = [.. validators];
        if (applicable.Length == 0)
        {
            return await next();
        }

        ValidationContext<TRequest> context = new(request);
        List<ValidationFailure> failures = [];

        foreach (IValidator<TRequest> validator in applicable)
        {
            ValidationResult result = await validator.ValidateAsync(context, cancellationToken);
            if (!result.IsValid)
            {
                failures.AddRange(result.Errors);
            }
        }

        if (failures.Count == 0)
        {
            return await next();
        }

        // Every failure is reported at once. Making a user fix one problem, resubmit,
        // and discover the next is a poor experience in a chat client, where the
        // command has to be retyped rather than edited.
        string description = string.Join(
            ' ',
            failures.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal));

        PipelineLog.ValidationRejected(logger, RequestName, description);

        return Failure(Error.Validation(FAILURE_CODE, description));
    }

    private static TResponse Failure(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        return (TResponse)FailureFactory.Value(error);
    }

    private static Func<Error, object> BuildFailureFactory()
    {
        if (!typeof(TResponse).IsGenericType
            || typeof(TResponse).GetGenericTypeDefinition() != typeof(Result<>))
        {
            throw new InvalidOperationException(
                $"'{typeof(TResponse)}' cannot represent a failure. A request must respond with "
                + $"{nameof(Result)} or {nameof(Result)}<T>.");
        }

        MethodInfo method = typeof(TResponse).GetMethod(
            nameof(Result<>.Failure),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(Error)])
            ?? throw new InvalidOperationException(
                $"'{typeof(TResponse)}' does not expose a static Failure(Error) method.");

        // Result<T> is a reference type, so the delegate's object return type is
        // reference-compatible with the method's and no wrapper is needed.
        return method.CreateDelegate<Func<Error, object>>();
    }
}
