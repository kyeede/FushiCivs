using System.Collections.Concurrent;
using System.Reflection;

using Fushi.Core.Errors;
using Fushi.Core.Results;

namespace Fushi.Application.Dispatching;

/// <summary>
/// Builds a failed result of a response type only known as a type parameter.
/// </summary>
/// <remarks>
/// A pipeline behaviour is generic over <c>TResponse</c>, so when validation
/// refuses a request the behaviour has to produce a failure of a type it cannot
/// name. There is no language construct for that, because <c>TResponse</c> could
/// be either <see cref="Result"/> or <see cref="Result{T}"/> and only the latter
/// has a type argument to supply.
/// <br/>
/// The reflection cost is paid once per closed response type: the static factory
/// method is turned into a delegate and cached, so subsequent calls are an
/// ordinary delegate invocation rather than a <c>MethodBase.Invoke</c>.
/// </remarks>
internal static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, Func<Error, object>> Factories = new();

    /// <summary>
    /// Creates a failed result of the given response type.
    /// </summary>
    /// <typeparam name="TResponse">
    /// The response type, which must be <see cref="Result"/> or
    /// <see cref="Result{T}"/>.
    /// </typeparam>
    /// <param name="error">The failure to carry.</param>
    /// <returns>A failed result of the requested type.</returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TResponse"/> is neither <see cref="Result"/> nor
    /// <see cref="Result{T}"/>. This means a request declared a response type
    /// the pipeline cannot fail, which is a definition error rather than a
    /// runtime condition.
    /// </exception>
    public static TResponse Failure<TResponse>(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        Func<Error, object> factory = Factories.GetOrAdd(typeof(TResponse), Build);
        return (TResponse)factory(error);
    }

    private static Func<Error, object> Build(Type responseType)
    {
        if (!responseType.IsGenericType
            || responseType.GetGenericTypeDefinition() != typeof(Result<>))
        {
            throw new InvalidOperationException(
                $"'{responseType}' cannot represent a failure. A request must respond with "
                + $"{nameof(Result)} or {nameof(Result)}<T>.");
        }

        MethodInfo method = responseType.GetMethod(
            nameof(Result<>.Failure),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(Error)])
            ?? throw new InvalidOperationException(
                $"'{responseType}' does not expose a static Failure(Error) method.");

        // Result<T> is a reference type, so the delegate's object return type is
        // reference-compatible with the method's and no wrapper is needed.
        return method.CreateDelegate<Func<Error, object>>();
    }
}
